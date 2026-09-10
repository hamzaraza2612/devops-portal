using DevOpsPortal.Domain.Entities;

namespace DevOpsPortal.Application.Abstractions;

public record GitCommitInfo(string Sha, string Message, string? AuthorName, string? AuthorEmail, DateTimeOffset? CommittedAt);

/// <summary>Wraps a provider call that may legitimately fail (network, auth, branch not
/// found) without that being an application error — callers show ErrorMessage rather
/// than treating this as an exception.</summary>
public record GitProviderResult<T>(bool Success, T? Data, string? ErrorMessage)
{
    public static GitProviderResult<T> Ok(T data) => new(true, data, null);

    public static GitProviderResult<T> Fail(string message) => new(false, default, message);
}

/// <summary>
/// Read-only commit lookup against a configured repository. Never mutates
/// anything in the Git provider and never triggers a deployment — a push
/// notification/webhook is out of scope for Phase 3; this is pull-based,
/// called only when a caller explicitly asks "what's the latest commit".
/// Implemented behind this interface so a non-GitLab provider (or a mock for
/// tests) can be substituted without touching any caller.
/// </summary>
public interface IGitProviderClient
{
    Task<GitProviderResult<GitCommitInfo>> GetLatestCommitAsync(Repository repository, string branch, CancellationToken cancellationToken = default);

    Task<GitProviderResult<IReadOnlyList<GitCommitInfo>>> GetRecentCommitsAsync(
        Repository repository, string branch, int count, CancellationToken cancellationToken = default);
}
