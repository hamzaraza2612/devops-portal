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
/// Commit lookup (read-only) and branch promotion (the one write operation
/// this interface performs) against a configured repository. Never triggers a
/// deployment — a push notification/webhook is out of scope; this is
/// pull-based/explicitly-invoked only. Implemented behind this interface so a
/// non-GitLab provider (or a mock for tests) can be substituted without
/// touching any caller.
/// </summary>
public interface IGitProviderClient
{
    Task<GitProviderResult<GitCommitInfo>> GetLatestCommitAsync(Repository repository, string branch, CancellationToken cancellationToken = default);

    Task<GitProviderResult<IReadOnlyList<GitCommitInfo>>> GetRecentCommitsAsync(
        Repository repository, string branch, int count, CancellationToken cancellationToken = default);

    /// <summary>Merges sourceBranch into targetBranch (e.g. "develop" -> "qa") — the git-level
    /// half of an environment promotion (see PromotionRequest.FromBranch/ToBranch). Requires a
    /// write-capable AccessTokenEnvVarName; without one this fails gracefully rather than
    /// attempting an unauthenticated write. Returns the resulting merge commit SHA on success.
    /// Never throws for an expected failure (network, auth, merge conflict, no token
    /// configured) — same GitProviderResult contract as the read methods above.</summary>
    Task<GitProviderResult<string>> PromoteBranchAsync(
        Repository repository, string sourceBranch, string targetBranch, CancellationToken cancellationToken = default);
}
