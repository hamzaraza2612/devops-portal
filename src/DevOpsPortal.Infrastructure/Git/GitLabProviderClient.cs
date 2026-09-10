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

    private static bool TryBuildCommitsUrl(Repository repository, string branch, int count, out string url, out string? error)
    {
        url = string.Empty;
        error = null;

        if (repository.Provider != RepositoryProvider.GitLab)
        {
            error = $"Commit lookup is not implemented for provider '{repository.Provider}'.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(branch))
        {
            error = "A branch name is required for commit lookup.";
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

        var encodedProject = Uri.EscapeDataString(projectPath);
        var perPage = Math.Clamp(count, 1, 50);
        url = $"{repoUri.Scheme}://{repoUri.Authority}/api/v4/projects/{encodedProject}/repository/commits" +
              $"?ref_name={Uri.EscapeDataString(branch)}&per_page={perPage}";
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
}
