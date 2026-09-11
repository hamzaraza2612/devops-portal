using DevOpsPortal.Application.Abstractions;

namespace DevOpsPortal.Infrastructure.Security;

/// <summary>Scoped (per-request or per-background-job) holder for the current tenant id.</summary>
public class AmbientTenantContext : IMutableTenantContext
{
    public Guid? TenantId { get; private set; }

    public void SetTenantId(Guid? tenantId) => TenantId = tenantId;
}
