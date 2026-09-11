using DevOpsPortal.Domain.Enums;

namespace DevOpsPortal.Domain.Entities;

/// <summary>
/// A reusable, configured build-provider connection (e.g. one Jenkins
/// instance) — the build-side counterpart of <see cref="TargetServer"/>.
/// Multiple applications' <see cref="BuildConfiguration"/> rows can reference
/// the same BuildServer. Holds no credentials: <see cref="ApiTokenEnvVarName"/>
/// is only the name of a server-side environment variable, read at call time,
/// never stored here and never returned by the API — mirrors
/// Repository.AccessTokenEnvVarName.
/// </summary>
public class BuildServer
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    public BuildProviderType ProviderType { get; set; } = BuildProviderType.Jenkins;

    /// <summary>Base URL of the build server, e.g. "https://jenkins.example.com".</summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>Jenkins username for basic auth. Not a secret by itself — the secret
    /// is the API token named by <see cref="ApiTokenEnvVarName"/>. Null means
    /// unauthenticated calls only.</summary>
    public string? Username { get; set; }

    /// <summary>Name of a server-side environment variable holding the Jenkins API
    /// token, e.g. "JENKINS_TOKEN_MAIN" — never the token itself.</summary>
    public string? ApiTokenEnvVarName { get; set; }

    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
