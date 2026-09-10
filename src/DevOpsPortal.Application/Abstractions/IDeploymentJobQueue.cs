namespace DevOpsPortal.Application.Abstractions;

/// <summary>
/// Hands a deployment ID off to the background worker so the HTTP request that
/// created it returns immediately (master requirements §6: "Deployment
/// execution must NOT block an HTTP request"). Deliberately minimal — an
/// in-process implementation is enough for a single-instance portal; the
/// interface is what lets a future phase swap in an out-of-process queue
/// (and a separate worker process/container) without touching any caller.
/// </summary>
public interface IDeploymentJobQueue
{
    void Enqueue(Guid deploymentId);

    Task<Guid> DequeueAsync(CancellationToken cancellationToken);
}
