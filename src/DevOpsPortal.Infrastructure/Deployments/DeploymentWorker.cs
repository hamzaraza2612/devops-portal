using DevOpsPortal.Application.Abstractions;
using DevOpsPortal.Application.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DevOpsPortal.Infrastructure.Deployments;

/// <summary>
/// Background job runner: dequeues one deployment job at a time and executes
/// it via IDeploymentExecutor in a fresh DI scope (IAppDbContext and friends
/// are scoped services). Runs in-process with the API for Phase 3 — see
/// PROJECT_STATE.md for why this isn't split into a separate worker
/// container yet. Deployments run one at a time in this process; per-app-
/// environment mutual exclusion is additionally enforced at request time by
/// IDeploymentService so this is not the only safety net.
///
/// Sets the job's TenantId on the scope's IMutableTenantContext before
/// resolving IDeploymentExecutor: this scope has no HTTP context, so nothing
/// would otherwise populate ICurrentTenantService, and AppDbContext's global
/// query filters would silently see every tenant-owned query return nothing.
/// </summary>
public class DeploymentWorker(
    IDeploymentJobQueue queue,
    IServiceScopeFactory scopeFactory,
    ILogger<DeploymentWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            DeploymentJob job;
            try
            {
                job = await queue.DequeueAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            using var scope = scopeFactory.CreateScope();
            scope.ServiceProvider.GetRequiredService<IMutableTenantContext>().SetTenantId(job.TenantId);
            var executor = scope.ServiceProvider.GetRequiredService<IDeploymentExecutor>();

            try
            {
                await executor.ExecuteAsync(job.DeploymentId, stoppingToken);
            }
            catch (Exception ex)
            {
                // The executor is responsible for marking the Deployment Failed on any
                // handled error; this catch only guards the worker loop itself against
                // an unexpected exception escaping, so one bad job can't kill the worker.
                logger.LogError(ex, "Unhandled exception executing deployment {DeploymentId}", job.DeploymentId);
            }
        }
    }
}
