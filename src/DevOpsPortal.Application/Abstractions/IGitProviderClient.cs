using DevOpsPortal.Domain.Entities;

namespace DevOpsPortal.Application.Abstractions;

public record GitCommitInfo(string Sha, string Message, string? AuthorName, string? AuthorEmail, DateTimeOffset? CommittedAt);

/// <summary>Result of a "Test GitLab Connection" check (master requirements §3):
/// actually reaches the configured GitLab instance and reports CONNECTED (with
/// the resolved project and, if a token is configured, the authenticated user)
/// or FAILED with a useful error message — never fabricated.</summary>
public record GitConnectionTestResult(bool Connected, string? AuthenticatedAs, string? ProjectName, string? ErrorMessage);

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

    /// <summary>Master requirements §3 "Test GitLab Connection": actually reaches
    /// the configured GitLab instance and verifies the project is reachable (and,
    /// if a token is configured, that it authenticates) — never fabricates a
    /// successful result.</summary>
    Task<GitConnectionTestResult> TestConnectionAsync(Repository repository, CancellationToken cancellationToken = default);

    /// <summary>Downloads a gzipped tarball of the repository's tree at
    /// <paramref name="refName"/> (a branch name or commit SHA) — the "obtain
    /// source" step of a LegacyFilesystem deployment that opts into
    /// ApplicationEnvironment.SyncSourceFromRepository. No local `git` binary is
    /// ever shelled out to: this calls GitLab's own
    /// `/repository/archive.tar.gz?sha=&lt;ref&gt;` REST endpoint, so the only
    /// external dependency is the already-configured HTTP(S) connection to
    /// GitLab. Same GitProviderResult contract as every other method here — a
    /// network failure, an invalid ref, or a private project with no token
    /// configured all come back as Fail, never a thrown exception.</summary>
    Task<GitProviderResult<byte[]>> DownloadRepositoryArchiveAsync(
        Repository repository, string refName, CancellationToken cancellationToken = default);
}
