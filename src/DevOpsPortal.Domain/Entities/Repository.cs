using DevOpsPortal.Domain.Enums;

namespace DevOpsPortal.Domain.Entities;

/// <summary>
/// A GitLab repository reference plus the portal-UI-configurable GitLab
/// connection details for it (master requirements: GitLab configuration
/// must be settable from the UI, never stored in Git). The access
/// credential itself lives only in the same encrypted secret store
/// <c>SecretReference</c> already uses — <see cref="AccessTokenStoreKey"/>
/// is an opaque reference, exactly like <c>SecretReference.StoreKey</c>.
/// </summary>
public class Repository
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public RepositoryProvider Provider { get; set; } = RepositoryProvider.GitLab;
    public string? Description { get; set; }

    /// <summary>Default/source branch this repository's applications build from
    /// (e.g. "main") — per-application-environment branch configuration on
    /// ApplicationEnvironment.BranchName always takes precedence where set; this is
    /// just the sensible default a new ApplicationEnvironment starts from.</summary>
    public string? DefaultBranch { get; set; }

    /// <summary>GitLab username/service-account name for the configured access
    /// token — display/audit only, never a credential by itself.</summary>
    public string? Username { get; set; }

    /// <summary>Opaque key into the encrypted secret store holding the actual GitLab
    /// access token — set via the portal's GitLab configuration UI, never the token
    /// value itself. Preferred over AccessTokenEnvVarName below when both are set.</summary>
    public string? AccessTokenStoreKey { get; set; }

    /// <summary>Legacy pattern, kept for backward compatibility with repositories
    /// configured before the encrypted-store option existed: the *name* of a
    /// server-side environment variable holding the token, never the token itself.
    /// Used only when AccessTokenStoreKey is not set.</summary>
    public string? AccessTokenEnvVarName { get; set; }

    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
