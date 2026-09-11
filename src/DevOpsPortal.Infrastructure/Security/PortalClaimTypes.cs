namespace DevOpsPortal.Infrastructure.Security;

public static class PortalClaimTypes
{
    public const string Permission = "permission";

    /// <summary>Carries User.TenantId. Absent entirely for a platform administrator
    /// (TenantId == null) — never emitted as an empty/sentinel value.</summary>
    public const string TenantId = "tenant_id";
}
