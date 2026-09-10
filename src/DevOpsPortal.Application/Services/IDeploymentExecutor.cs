namespace DevOpsPortal.Application.Services;

/// <summary>
/// Runs one queued deployment job to completion: re-validates, executes the
/// configured operations, health-checks, and finalizes the Deployment's
/// status. Invoked by the background worker in its own DI scope — never
/// called directly from an HTTP request.
/// </summary>
public interface IDeploymentExecutor
{
    Task ExecuteAsync(Guid deploymentId, CancellationToken cancellationToken);
}
