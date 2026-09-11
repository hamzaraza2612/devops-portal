namespace DevOpsPortal.Application.Abstractions;

/// <summary>Carries the tenant alongside the deployment id — the background worker's
/// DI scope has no HTTP context to resolve a tenant claim from, so it must be threaded
/// through explicitly (see DeploymentWorker).</summary>
public record DeploymentJob(Guid DeploymentId, Guid TenantId);

/// <summary>
/// Hands a deployment job off to the background worker so the HTTP request that
/// created it returns immediately (master requirements §6: "Deployment
/// execution must NOT block an HTTP request"). Deliberately minimal — an
/// in-process implementation is enough for a single-instance portal; the
/// interface is what lets a future phase swap in an out-of-process queue
/// (and a separate worker process/container) without touching any caller.
/// </summary>
public interface IDeploymentJobQueue
{
    void Enqueue(DeploymentJob job);

    Task<DeploymentJob> DequeueAsync(CancellationToken cancellationToken);
}
