using DevOpsPortal.Application.Abstractions;
using DevOpsPortal.Application.Exceptions;

namespace DevOpsPortal.Application.Common;

public static class CurrentTenantServiceExtensions
{
    /// <summary>Every tenant-owned entity's Create path needs a concrete TenantId to
    /// stamp on the new row. A platform administrator (TenantId == null) has no tenant
    /// to stamp — creating tenant-owned resources isn't a platform-admin action.</summary>
    public static Guid RequireTenantId(this ICurrentTenantService tenantService) =>
        tenantService.TenantId ?? throw new ForbiddenException("This action requires an authenticated tenant context.");
}
