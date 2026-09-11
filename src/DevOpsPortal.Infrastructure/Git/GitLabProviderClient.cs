using System.Net.Http.Json;
using System.Text.Json.Serialization;
using DevOpsPortal.Application.Abstractions;
using DevOpsPortal.Domain.Entities;
using DevOpsPortal.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace DevOpsPortal.Infrastructure.Git;

/// <summary>
/// Calls the GitLab REST API (v4) for read-only commit lookup. Authentication
/// is optional: if Repository.AccessTokenEnvVarName is set, the token is read
/// from that environment variable at call time (never stored in the DB, never
/// logged, never returned to a caller). Without a token, only public projects
/// are reachable. All failure modes (network, auth, 404, unsupported
/// provider) are returned as a Fail result — this never throws for expected
/// failures, so a GitLab outage can't break deployment request creation.
/// </summary>
public class GitLabProviderClient(HttpClient httpClient, ILogger<GitLabProviderClient> logger) : IGitProviderClient
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
        ApplyAuth(repository, request);

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

        var token = string.IsNullOrWhiteSpace(repository.AccessTokenEnvVarName)
            ? null
            : Environment.GetEnvironmentVariable(repository.AccessTokenEnvVarName);
        if (string.IsNullOrWhiteSpace(token))
        {
            return GitProviderResult<string>.Fail(
                "Branch promotion requires a write-capable AccessTokenEnvVarName configured on this repository — none is set.");
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

    private static void ApplyAuth(Repository repository, HttpRequestMessage request)
    {
        if (string.IsNullOrWhiteSpace(repository.AccessTokenEnvVarName))
            return;

        var token = Environment.GetEnvironmentVariable(repository.AccessTokenEnvVarName);
        if (!string.IsNullOrWhiteSpace(token))
            request.Headers.Add("PRIVATE-TOKEN", token);
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
}
