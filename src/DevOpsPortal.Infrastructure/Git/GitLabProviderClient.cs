using System.Net.Http.Json;
using System.Text.Json.Serialization;
using DevOpsPortal.Application.Abstractions;
using DevOpsPortal.Domain.Entities;
using DevOpsPortal.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace DevOpsPortal.Infrastructure.Git;

/// <summary>
/// Calls the GitLab REST API (v4) for read-only commit lookup. Authentication
/// is optional: Repository.AccessTokenStoreKey (set via the portal's GitLab
/// configuration UI, resolved through the same ISecretProvider every other
/// credential in the portal uses) is preferred when set; Repository.
/// AccessTokenEnvVarName is the legacy fallback for repositories configured
/// before the encrypted-store option existed — never stored in the DB as
/// plaintext, never logged, never returned to a caller. Without a token, only
/// public projects are reachable. All failure modes (network, auth, 404,
/// unsupported provider) are returned as a Fail result — this never throws for
/// expected failures, so a GitLab outage can't break deployment request
/// creation.
/// </summary>
public class GitLabProviderClient(HttpClient httpClient, ISecretProvider secretProvider, ILogger<GitLabProviderClient> logger) : IGitProviderClient
{
    public async Task<GitProviderResult<GitCommitInfo>> GetLatestCommitAsync(
        Repository repository, string branch, CancellationToken cancellationToken = default)
    {
        var result = await GetRecentCommitsAsync(repository, branch, 1, cancellationToken);
        if (!result.Success)
            return GitProviderResult<GitCommitInfo>.Fail(result.ErrorMessage!);

        var commit = result.Data!.FirstOrDefault();
        return commit is null
            ? GitProviderResult<GitCommitInfo>.Fail($"No commits found on branch '{branch}'.")
            : GitProviderResult<GitCommitInfo>.Ok(commit);
    }

    public async Task<GitProviderResult<IReadOnlyList<GitCommitInfo>>> GetRecentCommitsAsync(
        Repository repository, string branch, int count, CancellationToken cancellationToken = default)
    {
        if (!TryBuildCommitsUrl(repository, branch, count, out var url, out var buildError))
            return GitProviderResult<IReadOnlyList<GitCommitInfo>>.Fail(buildError!);

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        await ApplyAuthAsync(repository, request, cancellationToken);

        try
        {
            using var response = await httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "GitLab commit lookup for repository {RepositoryName} returned {StatusCode}", repository.Name, response.StatusCode);
                return GitProviderResult<IReadOnlyList<GitCommitInfo>>.Fail(
                    $"GitLab returned {(int)response.StatusCode} {response.ReasonPhrase} — check the branch name and, for private projects, AccessTokenEnvVarName.");
            }

            var payload = await response.Content.ReadFromJsonAsync<List<GitLabCommitDto>>(cancellationToken) ?? [];
            IReadOnlyList<GitCommitInfo> commits = payload
                .Select(c => new GitCommitInfo(c.Id, c.Title ?? c.Message ?? string.Empty, c.AuthorName, c.AuthorEmail, c.CommittedDate))
                .ToList();
            return GitProviderResult<IReadOnlyList<GitCommitInfo>>.Ok(commits);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(ex, "GitLab commit lookup failed for repository {RepositoryName}", repository.Name);
            return GitProviderResult<IReadOnlyList<GitCommitInfo>>.Fail("Could not reach the configured GitLab instance.");
        }
    }

    /// <summary>Creates (or reuses an already-open) merge request from sourceBranch into
    /// targetBranch and immediately accepts it — the closest GitLab REST API equivalent of a
    /// direct "merge branch A into B". Requires AccessTokenEnvVarName: GitLab has no anonymous
    /// write path, so without a token this fails fast rather than attempting (and 401-ing) a
    /// real request.</summary>
    public async Task<GitProviderResult<string>> PromoteBranchAsync(
        Repository repository, string sourceBranch, string targetBranch, CancellationToken cancellationToken = default)
    {
        if (!TryBuildProjectApiBase(repository, out var apiBase, out var buildError))
            return GitProviderResult<string>.Fail(buildError!);

        if (string.IsNullOrWhiteSpace(sourceBranch) || string.IsNullOrWhiteSpace(targetBranch))
            return GitProviderResult<string>.Fail("Both a source and target branch are required for branch promotion.");

        var token = await ResolveAccessTokenAsync(repository, cancellationToken);
        if (string.IsNullOrWhiteSpace(token))
        {
            return GitProviderResult<string>.Fail(
                "Branch promotion requires a write-capable access token configured on this repository (via the GitLab configuration UI, or legacy AccessTokenEnvVarName) — none is set.");
        }

        try
        {
            var mergeRequestIid = await FindOrCreateOpenMergeRequestAsync(apiBase, token, sourceBranch, targetBranch, repository.Name, cancellationToken);
            if (mergeRequestIid is null)
                return GitProviderResult<string>.Fail("Could not create or locate a merge request for this branch pair.");

            using var acceptRequest = new HttpRequestMessage(HttpMethod.Put, $"{apiBase}/merge_requests/{mergeRequestIid}/merge");
            acceptRequest.Headers.Add("PRIVATE-TOKEN", token);
            using var acceptResponse = await httpClient.SendAsync(acceptRequest, cancellationToken);
            if (!acceptResponse.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "GitLab branch promotion for {RepositoryName} ({Source} -> {Target}) failed to merge: {StatusCode}",
                    repository.Name, sourceBranch, targetBranch, acceptResponse.StatusCode);
                return GitProviderResult<string>.Fail(
                    $"GitLab could not merge '{sourceBranch}' into '{targetBranch}' ({(int)acceptResponse.StatusCode} {acceptResponse.ReasonPhrase}) — likely a merge conflict.");
            }

            var merged = await acceptResponse.Content.ReadFromJsonAsync<GitLabMergeRequestDto>(cancellationToken);
            return GitProviderResult<string>.Ok(merged?.MergeCommitSha ?? merged?.Sha ?? "merged");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(ex, "GitLab branch promotion failed for repository {RepositoryName}", repository.Name);
            return GitProviderResult<string>.Fail("Could not reach the configured GitLab instance.");
        }
    }

    private async Task<int?> FindOrCreateOpenMergeRequestAsync(
        string apiBase, string token, string sourceBranch, string targetBranch, string repositoryName, CancellationToken cancellationToken)
    {
        using var createRequest = new HttpRequestMessage(HttpMethod.Post, $"{apiBase}/merge_requests");
        createRequest.Headers.Add("PRIVATE-TOKEN", token);
        createRequest.Content = JsonContent.Create(new
        {
            source_branch = sourceBranch,
            target_branch = targetBranch,
            title = $"Promote {sourceBranch} to {targetBranch}",
            remove_source_branch = false,
        });

        using var createResponse = await httpClient.SendAsync(createRequest, cancellationToken);
        if (createResponse.IsSuccessStatusCode)
        {
            var created = await createResponse.Content.ReadFromJsonAsync<GitLabMergeRequestDto>(cancellationToken);
            return created?.Iid;
        }

        // 409 Conflict: GitLab already has an open MR for this exact source/target pair —
        // reuse it instead of treating this as a failure.
        if (createResponse.StatusCode != System.Net.HttpStatusCode.Conflict)
        {
            logger.LogWarning(
                "GitLab branch promotion for {RepositoryName} could not open a merge request ({Source} -> {Target}): {StatusCode}",
                repositoryName, sourceBranch, targetBranch, createResponse.StatusCode);
            return null;
        }

        using var listRequest = new HttpRequestMessage(
            HttpMethod.Get,
            $"{apiBase}/merge_requests?state=opened&source_branch={Uri.EscapeDataString(sourceBranch)}&target_branch={Uri.EscapeDataString(targetBranch)}");
        listRequest.Headers.Add("PRIVATE-TOKEN", token);
        using var listResponse = await httpClient.SendAsync(listRequest, cancellationToken);
        if (!listResponse.IsSuccessStatusCode)
            return null;

        var existing = await listResponse.Content.ReadFromJsonAsync<List<GitLabMergeRequestDto>>(cancellationToken) ?? [];
        return existing.FirstOrDefault()?.Iid;
    }

    private static bool TryBuildProjectApiBase(Repository repository, out string apiBase, out string? error)
    {
        apiBase = string.Empty;
        error = null;

        if (repository.Provider != RepositoryProvider.GitLab)
        {
            error = $"This operation is not implemented for provider '{repository.Provider}'.";
            return false;
        }

        if (!Uri.TryCreate(repository.Url, UriKind.Absolute, out var repoUri))
        {
            error = "Repository URL is not a valid absolute URL.";
            return false;
        }

        var projectPath = repoUri.AbsolutePath.Trim('/');
        if (projectPath.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
            projectPath = projectPath[..^4];
        if (projectPath.Length == 0)
        {
            error = "Repository URL has no project path.";
            return false;
        }

        apiBase = $"{repoUri.Scheme}://{repoUri.Authority}/api/v4/projects/{Uri.EscapeDataString(projectPath)}";
        return true;
    }

    private static bool TryBuildCommitsUrl(Repository repository, string branch, int count, out string url, out string? error)
    {
        url = string.Empty;

        if (!TryBuildProjectApiBase(repository, out var apiBase, out error))
            return false;

        if (string.IsNullOrWhiteSpace(branch))
        {
            error = "A branch name is required for commit lookup.";
            return false;
        }

        var perPage = Math.Clamp(count, 1, 50);
        url = $"{apiBase}/repository/commits?ref_name={Uri.EscapeDataString(branch)}&per_page={perPage}";
        return true;
    }

    /// <summary>AccessTokenStoreKey (the encrypted-store reference, set via the
    /// portal's GitLab configuration UI) takes precedence when set; falls back to
    /// the legacy AccessTokenEnvVarName for repositories configured before that
    /// option existed.</summary>
    private async Task<string?> ResolveAccessTokenAsync(Repository repository, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(repository.AccessTokenStoreKey))
        {
            var result = await secretProvider.RetrieveAsync(repository.AccessTokenStoreKey, cancellationToken);
            if (result.Success && !string.IsNullOrEmpty(result.Value))
                return result.Value;
        }

        return string.IsNullOrWhiteSpace(repository.AccessTokenEnvVarName)
            ? null
            : Environment.GetEnvironmentVariable(repository.AccessTokenEnvVarName);
    }

    private async Task ApplyAuthAsync(Repository repository, HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var token = await ResolveAccessTokenAsync(repository, cancellationToken);
        if (!string.IsNullOrWhiteSpace(token))
            request.Headers.Add("PRIVATE-TOKEN", token);
    }

    /// <summary>GETs the project itself (validates the URL/project path, and —
    /// if a token is configured — that it authenticates for at least read
    /// access), then GETs /user with the same token to report who it belongs to.
    /// Never fabricates CONNECTED — any failure at either step is reported as-is.</summary>
    public async Task<GitConnectionTestResult> TestConnectionAsync(Repository repository, CancellationToken cancellationToken = default)
    {
        if (!TryBuildProjectApiBase(repository, out var apiBase, out var buildError))
            return new GitConnectionTestResult(false, null, null, buildError);

        using var projectRequest = new HttpRequestMessage(HttpMethod.Get, apiBase);
        await ApplyAuthAsync(repository, projectRequest, cancellationToken);

        try
        {
            using var projectResponse = await httpClient.SendAsync(projectRequest, cancellationToken);
            if (!projectResponse.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "GitLab connection test for repository {RepositoryName} returned {StatusCode}", repository.Name, projectResponse.StatusCode);
                return new GitConnectionTestResult(false, null, null,
                    $"GitLab returned {(int)projectResponse.StatusCode} {projectResponse.ReasonPhrase} — check the URL and, for private projects, the access token.");
            }

            var project = await projectResponse.Content.ReadFromJsonAsync<GitLabProjectDto>(cancellationToken);

            string? authenticatedAs = null;
            var token = await ResolveAccessTokenAsync(repository, cancellationToken);
            if (!string.IsNullOrWhiteSpace(token) && Uri.TryCreate(repository.Url, UriKind.Absolute, out var repoUri))
            {
                using var userRequest = new HttpRequestMessage(HttpMethod.Get, $"{repoUri.Scheme}://{repoUri.Authority}/api/v4/user");
                userRequest.Headers.Add("PRIVATE-TOKEN", token);
                using var userResponse = await httpClient.SendAsync(userRequest, cancellationToken);
                if (userResponse.IsSuccessStatusCode)
                {
                    var user = await userResponse.Content.ReadFromJsonAsync<GitLabUserDto>(cancellationToken);
                    authenticatedAs = user?.Username;
                }
            }

            return new GitConnectionTestResult(true, authenticatedAs, project?.PathWithNamespace ?? project?.Name, null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(ex, "GitLab connection test failed for repository {RepositoryName}", repository.Name);
            return new GitConnectionTestResult(false, null, null, "Could not reach the configured GitLab instance.");
        }
    }

    /// <summary>See IGitProviderClient.DownloadRepositoryArchiveAsync. GitLab's
    /// archive endpoint returns the gzipped tarball directly (no JSON envelope) —
    /// this reads the raw bytes rather than deserializing anything.</summary>
    public async Task<GitProviderResult<byte[]>> DownloadRepositoryArchiveAsync(
        Repository repository, string refName, CancellationToken cancellationToken = default)
    {
        if (!TryBuildProjectApiBase(repository, out var apiBase, out var buildError))
            return GitProviderResult<byte[]>.Fail(buildError!);

        if (string.IsNullOrWhiteSpace(refName))
            return GitProviderResult<byte[]>.Fail("A branch name or commit SHA is required to download a repository archive.");

        var url = $"{apiBase}/repository/archive.tar.gz?sha={Uri.EscapeDataString(refName)}";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        await ApplyAuthAsync(repository, request, cancellationToken);

        try
        {
            using var response = await httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "GitLab repository archive download for {RepositoryName}@{Ref} returned {StatusCode}", repository.Name, refName, response.StatusCode);
                return GitProviderResult<byte[]>.Fail(
                    $"GitLab returned {(int)response.StatusCode} {response.ReasonPhrase} downloading the archive for '{refName}' — " +
                    "check the branch/commit name and, for private projects, that the access token has repository read access.");
            }

            var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            if (bytes.Length == 0)
                return GitProviderResult<byte[]>.Fail($"GitLab returned an empty archive for '{refName}'.");

            return GitProviderResult<byte[]>.Ok(bytes);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(ex, "GitLab repository archive download failed for repository {RepositoryName}", repository.Name);
            // A TaskCanceledException whose cause is NOT our own cancellationToken means
            // HttpClient's own Timeout fired mid-download — a genuinely different problem
            // (and fix) from GitLab being unreachable, so don't report it as the latter.
            var message = ex is TaskCanceledException && !cancellationToken.IsCancellationRequested
                ? $"Downloading the repository archive for '{refName}' timed out after {httpClient.Timeout.TotalSeconds:0}s. " +
                  "The archive may be too large (old deployment backups committed into the repository often bloat it), " +
                  "or the GitLab instance is slow to respond."
                : "Could not reach the configured GitLab instance.";
            return GitProviderResult<byte[]>.Fail(message);
        }
    }

    private static readonly HashSet<string> ComposeFileNames = new(StringComparer.OrdinalIgnoreCase) { "docker-compose.yml", "docker-compose.yaml" };

    /// <summary>See IGitProviderClient.ListRepositoryFoldersAsync. Deliberately
    /// two shallow, non-recursive scans rather than one `recursive=true` call:
    /// (1) list only the repository's top-level entries, then (2) for each
    /// top-level folder, list only *that folder's own direct children* to check
    /// for a compose file. A single recursive call was tried first and found to
    /// be actively wrong for a real monorepo layout — GitLab's recursive tree
    /// walks depth-first, so a folder's own `docker-compose.yml` can sit behind
    /// an arbitrarily large `Backups/` (or similar) subdirectory full of old
    /// deployment snapshots, meaning a page-count cap on the recursive listing
    /// can exhaust itself deep inside the first folder's Backups tree and never
    /// even reach a single compose file. This N+1-call approach never descends
    /// into any subdirectory, so it can never get lost in one.</summary>
    public async Task<GitProviderResult<IReadOnlyList<GitRepositoryFolder>>> ListRepositoryFoldersAsync(
        Repository repository, string refName, CancellationToken cancellationToken = default)
    {
        if (!TryBuildProjectApiBase(repository, out var apiBase, out var buildError))
            return GitProviderResult<IReadOnlyList<GitRepositoryFolder>>.Fail(buildError!);

        if (string.IsNullOrWhiteSpace(refName))
            return GitProviderResult<IReadOnlyList<GitRepositoryFolder>>.Fail("A branch name or commit SHA is required to list repository folders.");

        var topLevelResult = await ListTreeAsync(apiBase, repository, refName, path: null, cancellationToken);
        if (!topLevelResult.Success)
            return GitProviderResult<IReadOnlyList<GitRepositoryFolder>>.Fail(topLevelResult.ErrorMessage!);

        var topLevelFolders = topLevelResult.Data!
            .Where(e => e.Type == "tree" && !string.IsNullOrEmpty(e.Path))
            .Select(e => e.Path)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            // A hard cap on how many folders get their own direct-children lookup —
            // generous for any real monorepo, but bounds worst-case request count.
            .Take(500)
            .ToList();

        var folders = new List<GitRepositoryFolder>();
        foreach (var folder in topLevelFolders)
        {
            var childrenResult = await ListTreeAsync(apiBase, repository, refName, folder, cancellationToken);
            if (!childrenResult.Success)
                return GitProviderResult<IReadOnlyList<GitRepositoryFolder>>.Fail(childrenResult.ErrorMessage!);

            var hasComposeFile = childrenResult.Data!.Any(e => e.Type == "blob" && ComposeFileNames.Contains(e.Name));
            folders.Add(new GitRepositoryFolder(folder, hasComposeFile));
        }

        return GitProviderResult<IReadOnlyList<GitRepositoryFolder>>.Ok(folders);
    }

    /// <summary>One non-recursive `/repository/tree` page (GitLab defaults to
    /// 20 entries per page without `per_page`, so this always passes a generous
    /// explicit value) — <paramref name="path"/> null lists the repository
    /// root; set, it lists only that path's own direct children, never
    /// anything nested deeper. Paginated in case a single directory has more
    /// than one page of direct children (a top-level scan with hundreds of
    /// application folders, for instance).</summary>
    private async Task<GitProviderResult<List<GitLabTreeEntryDto>>> ListTreeAsync(
        string apiBase, Repository repository, string refName, string? path, CancellationToken cancellationToken)
    {
        const int perPage = 100;
        const int maxPages = 10; // 1000 direct children of one path is generous; never applies to the whole-tree case since this is always non-recursive
        var entries = new List<GitLabTreeEntryDto>();

        for (var page = 1; page <= maxPages; page++)
        {
            var pathQuery = string.IsNullOrEmpty(path) ? string.Empty : $"&path={Uri.EscapeDataString(path)}";
            var url = $"{apiBase}/repository/tree?ref={Uri.EscapeDataString(refName)}{pathQuery}&per_page={perPage}&page={page}";
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            await ApplyAuthAsync(repository, request, cancellationToken);

            try
            {
                using var response = await httpClient.SendAsync(request, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    logger.LogWarning(
                        "GitLab repository tree listing for {RepositoryName}@{Ref} ({Path}) returned {StatusCode}",
                        repository.Name, refName, path ?? "/", response.StatusCode);
                    return GitProviderResult<List<GitLabTreeEntryDto>>.Fail(
                        $"GitLab returned {(int)response.StatusCode} {response.ReasonPhrase} listing '{path ?? refName}'.");
                }

                var pageEntries = await response.Content.ReadFromJsonAsync<List<GitLabTreeEntryDto>>(cancellationToken) ?? [];
                entries.AddRange(pageEntries);
                if (pageEntries.Count < perPage)
                    break;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                logger.LogWarning(ex, "GitLab repository tree listing failed for repository {RepositoryName}", repository.Name);
                return GitProviderResult<List<GitLabTreeEntryDto>>.Fail("Could not reach the configured GitLab instance.");
            }
        }

        return GitProviderResult<List<GitLabTreeEntryDto>>.Ok(entries);
    }

    private sealed class GitLabTreeEntryDto
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("type")]
        public string Type { get; set; } = string.Empty;

        [JsonPropertyName("path")]
        public string Path { get; set; } = string.Empty;
    }

    private sealed class GitLabCommitDto
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("title")]
        public string? Title { get; set; }

        [JsonPropertyName("message")]
        public string? Message { get; set; }

        [JsonPropertyName("author_name")]
        public string? AuthorName { get; set; }

        [JsonPropertyName("author_email")]
        public string? AuthorEmail { get; set; }

        [JsonPropertyName("committed_date")]
        public DateTimeOffset? CommittedDate { get; set; }
    }

    private sealed class GitLabMergeRequestDto
    {
        [JsonPropertyName("iid")]
        public int Iid { get; set; }

        [JsonPropertyName("merge_commit_sha")]
        public string? MergeCommitSha { get; set; }

        [JsonPropertyName("sha")]
        public string? Sha { get; set; }
    }

    private sealed class GitLabProjectDto
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("path_with_namespace")]
        public string? PathWithNamespace { get; set; }
    }

    private sealed class GitLabUserDto
    {
        [JsonPropertyName("username")]
        public string? Username { get; set; }
    }
}
