namespace DevOpsPortal.Application.Abstractions;

/// <summary>
/// Resolved once per DI scope — from the JWT tenant claim for an HTTP request,
/// or explicitly set by DeploymentWorker for a background deployment job — and
/// read by AppDbContext's global query filters on every tenant-owned entity.
/// Null means "platform-level": no tenant-owned rows are visible.
/// </summary>
public interface ICurrentTenantService
{
    Guid? TenantId { get; }
}
