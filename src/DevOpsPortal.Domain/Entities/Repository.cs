using DevOpsPortal.Domain.Enums;

namespace DevOpsPortal.Domain.Entities;

/// <summary>
/// A Git repository reference. Holds no credentials (see master requirements
/// §14) — repository access secrets are a separate, later concern.
/// </summary>
public class Repository
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public RepositoryProvider Provider { get; set; } = RepositoryProvider.GitLab;
    public string? Description { get; set; }

    /// <summary>Name of a server-side environment variable holding a GitLab access
    /// token, e.g. "GITLAB_TOKEN_DMSAPI" — never the token itself. Left null,
    /// commit lookup is attempted unauthenticated (works for public projects only).
    /// The actual token is supplied externally as a container env var and is
    /// never stored in, or returned by, this API.</summary>
    public string? AccessTokenEnvVarName { get; set; }

    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
