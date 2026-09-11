namespace DevOpsPortal.Domain.Constants;

public static class RoleNames
{
    /// <summary>System-wide (Role.TenantId == null), seeded once at platform startup —
    /// distinct from the per-tenant "ADMIN" role every tenant gets provisioned with
    /// (see TenantService). Only a platform administrator ever holds this role.</summary>
    public const string PlatformAdmin = "PLATFORM_ADMIN";

    public const string Admin = "ADMIN";
    public const string DevOps = "DEVOPS";
    public const string Developer = "DEVELOPER";
    public const string Qa = "QA";
    public const string Uat = "UAT";
    public const string Cto = "CTO";

    /// <summary>The default role set every new tenant is provisioned with — never
    /// includes PlatformAdmin, which is global and not tenant-owned.</summary>
    public static readonly IReadOnlyList<string> All = new[] { Admin, DevOps, Developer, Qa, Uat, Cto };
}
