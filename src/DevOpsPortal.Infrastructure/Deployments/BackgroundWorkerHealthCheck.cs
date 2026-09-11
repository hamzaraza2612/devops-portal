using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;

namespace DevOpsPortal.Infrastructure.Deployments;

/// <summary>
/// Reports whether DeploymentWorker's background loop is still running, by
/// inspecting BackgroundService.ExecuteTask (public since .NET Core 3.0) on
/// the registered IHostedService instance — no separate heartbeat/polling
/// mechanism needed. The worker's own catch-all around each job (see
/// DeploymentWorker) means ExecuteTask should never actually fault, but this
/// still catches the case where the process itself failed to start the
/// loop (e.g. a DI resolution error) or the host is shutting down.
/// </summary>
public class BackgroundWorkerHealthCheck(IEnumerable<IHostedService> hostedServices) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var worker = hostedServices.OfType<DeploymentWorker>().FirstOrDefault();
        if (worker is null)
            return Task.FromResult(HealthCheckResult.Unhealthy("Deployment worker is not registered."));

        var task = worker.ExecuteTask;
        if (task is null)
            return Task.FromResult(HealthCheckResult.Unhealthy("Deployment worker has not started."));

        if (task.IsCompleted)
        {
            return Task.FromResult(task.IsFaulted
                ? HealthCheckResult.Unhealthy("Deployment worker loop has crashed.", task.Exception)
                : HealthCheckResult.Unhealthy("Deployment worker loop has stopped."));
        }

        return Task.FromResult(HealthCheckResult.Healthy("Deployment worker loop is running."));
    }
}
