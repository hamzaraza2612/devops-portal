namespace DevOpsPortal.Domain.Enums;

/// <summary>
/// How broadly a SecretReference is available — the structural mechanism
/// behind environment isolation (master requirements §5: "a secret
/// configured for DEV must not automatically be available to QA/UAT/
/// Production"). Ordered by specificity: a higher numeric value is more
/// specific and wins when resolving a name that exists at more than one
/// scope (see SecretReferenceService.ResolveForDeploymentAsync).
/// </summary>
public enum SecretScope
{
    /// <summary>Not tied to any application or environment (e.g. platform SMTP).
    /// ApplicationId and EnvironmentDefinitionId must both be null.</summary>
    Global = 0,

    /// <summary>Shared across every environment of one application (e.g. a
    /// GitLab token for that app's repository). ApplicationId is required;
    /// EnvironmentDefinitionId must be null.</summary>
    Application = 1,

    /// <summary>Available only to one specific application+environment pair
    /// (e.g. a PROD-only database password). Both ApplicationId and
    /// EnvironmentDefinitionId are required. This is the scope that makes
    /// environment isolation concrete: a secret scoped to DEV here is never
    /// visible when resolving QA/UAT/Production.</summary>
    ApplicationEnvironment = 2,
}
