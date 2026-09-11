namespace DevOpsPortal.Application.Abstractions;

/// <summary>
/// The settable side of the ambient tenant context. Used by the two things
/// that populate it: a request middleware (from the JWT tenant claim) and
/// DeploymentWorker (from the queued job's TenantId, since a background job's
/// DI scope has no HTTP context to read a claim from).
/// </summary>
public interface IMutableTenantContext : ICurrentTenantService
{
    void SetTenantId(Guid? tenantId);
}
