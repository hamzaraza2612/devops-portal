using DevOpsPortal.Application.Abstractions;
using DevOpsPortal.Application.Common;
using DevOpsPortal.Application.Exceptions;
using DevOpsPortal.Domain.Entities;
using DevOpsPortal.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DevOpsPortal.Application.Services;

/// <summary>
/// Runs a single queued Deployment: re-validates server-side (config can have
/// changed since the request was created), executes the configured
/// operations, health-checks, and finalizes status. Every step is logged
/// (sanitized) to DeploymentLogEntry. Never invoked from an HTTP request —
/// only from DeploymentWorker in its own DI scope.
/// </summary>
public class DeploymentExecutor(
    IAppDbContext db,
    IComposeCommandExecutor composeExecutor,
    IHealthCheckProbe healthCheckProbe,
    IAuditService auditService,
    ILogger<DeploymentExecutor> logger) : IDeploymentExecutor
{
    public async Task ExecuteAsync(Guid deploymentId, CancellationToken cancellationToken)
    {
        var deployment = await db.Deployments
            .Include(d => d.Application)
            .Include(d => d.EnvironmentDefinition)
            .Include(d => d.ApplicationEnvironment).ThenInclude(ae => ae.TargetServer).ThenInclude(ts => ts.AllowedDeploymentRoots)
            .FirstOrDefaultAsync(d => d.Id == deploymentId, cancellationToken);

        if (deployment is null)
        {
            logger.LogError("DeploymentExecutor: deployment {DeploymentId} was not found", deploymentId);
            return;
        }

        if (deployment.Status is not (DeploymentStatus.Pending or DeploymentStatus.Queued))
        {
            logger.LogWarning("DeploymentExecutor: deployment {DeploymentId} is already {Status}; skipping", deploymentId, deployment.Status);
            return;
        }

        var log = new DeploymentLogWriter(db, deployment.Id);

        deployment.Status = DeploymentStatus.Running;
        deployment.StartedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        await log.WriteAsync(DeploymentLogLevel.Info,
            $"Deployment started for {deployment.Application.Name}/{deployment.EnvironmentDefinition.Name} at commit {deployment.CommitSha}.",
            cancellationToken);

        try
        {
            var appEnv = deployment.ApplicationEnvironment;
            if (!appEnv.IsActive)
                throw new DeploymentExecutionException("The application-environment configuration is no longer active.");

            if (deployment.Application.DeploymentMode == DeploymentMode.LegacyFilesystem)
            {
                await ExecuteLegacyFilesystemAsync(appEnv, log, cancellationToken);
            }
            else
            {
                throw new DeploymentExecutionException(
                    "Container-image deployment execution is not implemented yet (no build/registry pipeline exists in this phase). " +
                    "Switch this application to LegacyFilesystem mode, or wait for the build-engine phase.");
            }

            await log.WriteAsync(DeploymentLogLevel.Info, "Running post-deployment health check.", cancellationToken);
            var healthResult = await healthCheckProbe.ProbeAsync(
                appEnv.HealthCheckType, appEnv.HealthCheckEndpoint, appEnv.HealthCheckTimeoutSeconds, cancellationToken);

            deployment.HealthCheckPassed = healthResult.Passed;
            deployment.HealthCheckDetail = LogSanitizer.Sanitize(healthResult.Detail);
            await log.WriteAsync(
                healthResult.Passed ? DeploymentLogLevel.Info : DeploymentLogLevel.Error,
                $"Health check result: {healthResult.Detail}", cancellationToken);

            if (!healthResult.Passed)
                throw new DeploymentExecutionException($"Health check failed: {healthResult.Detail}");

            deployment.Status = DeploymentStatus.Succeeded;
            deployment.CompletedAt = DateTimeOffset.UtcNow;
            await log.WriteAsync(DeploymentLogLevel.Info, "Deployment succeeded.", cancellationToken);
            await db.SaveChangesAsync(cancellationToken);

            await auditService.LogAsync(deployment.IsRollback ? "rollback.succeeded" : "deployment.succeeded", AuditResult.Success,
                "Deployment", deployment.Id.ToString(),
                details: $"{deployment.Application.Name}/{deployment.EnvironmentDefinition.Name} commit {deployment.CommitSha}",
                cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            deployment.Status = DeploymentStatus.Failed;
            deployment.CompletedAt = DateTimeOffset.UtcNow;
            deployment.FailureReason = LogSanitizer.Sanitize(ex.Message);
            await log.WriteAsync(DeploymentLogLevel.Error, $"Deployment failed: {ex.Message}", cancellationToken);
            await db.SaveChangesAsync(cancellationToken);

            await auditService.LogAsync(deployment.IsRollback ? "rollback.failed" : "deployment.failed", AuditResult.Failure,
                "Deployment", deployment.Id.ToString(),
                details: $"{deployment.Application.Name}/{deployment.EnvironmentDefinition.Name} commit {deployment.CommitSha}: {LogSanitizer.Sanitize(ex.Message)}",
                cancellationToken: cancellationToken);
        }
    }

    private async Task ExecuteLegacyFilesystemAsync(ApplicationEnvironment appEnv, DeploymentLogWriter log, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(appEnv.DeploymentRootPath))
            throw new DeploymentExecutionException("DeploymentRootPath is not configured.");

        // Re-validate the allow-list here too — config could have changed between
        // request creation and this job actually running.
        var activeRoots = appEnv.TargetServer.AllowedDeploymentRoots.Where(r => r.IsActive).Select(r => r.RootPath);
        if (!DeploymentPathValidator.IsUnderAllowedRoot(appEnv.DeploymentRootPath, activeRoots))
            throw new DeploymentExecutionException("DeploymentRootPath is no longer under an allowed deployment root for its target server.");

        var downOperation = appEnv.UseDownWithVolumesOnDeploy ? ComposeOperation.DownWithVolumes : ComposeOperation.Down;
        await log.WriteAsync(DeploymentLogLevel.Info,
            $"Restarting compose stack at '{appEnv.DeploymentRootPath}' ({appEnv.ComposeFilePath}), " +
            $"down={(downOperation == ComposeOperation.DownWithVolumes ? "down -v" : "down")}.", cancellationToken);

        var downResult = await composeExecutor.RunAsync(
            new ComposeCommandRequest(appEnv.DeploymentRootPath, appEnv.ComposeFilePath, appEnv.ComposeProjectName, downOperation), cancellationToken);
        await log.WriteAsync(
            downResult.Success ? DeploymentLogLevel.Info : DeploymentLogLevel.Warning,
            $"compose down: exit {downResult.ExitCode}\n{Truncate(downResult.StandardOutput)}\n{Truncate(downResult.StandardError)}",
            cancellationToken);
        // A failing "down" (e.g. the stack wasn't running yet) is not fatal — proceed to "up".

        var upResult = await composeExecutor.RunAsync(
            new ComposeCommandRequest(appEnv.DeploymentRootPath, appEnv.ComposeFilePath, appEnv.ComposeProjectName, ComposeOperation.Up), cancellationToken);
        await log.WriteAsync(
            upResult.Success ? DeploymentLogLevel.Info : DeploymentLogLevel.Error,
            $"compose up -d: exit {upResult.ExitCode}\n{Truncate(upResult.StandardOutput)}\n{Truncate(upResult.StandardError)}",
            cancellationToken);

        if (!upResult.Success)
            throw new DeploymentExecutionException($"docker compose up failed (exit code {upResult.ExitCode}).");
    }

    private static string Truncate(string s) => s.Length > 4000 ? s[..4000] + "... (truncated)" : s;

    /// <summary>Small stateful helper so log entries get a correctly incrementing
    /// Sequence without needing a `ref` parameter (not allowed across `await`).</summary>
    private sealed class DeploymentLogWriter(IAppDbContext db, Guid deploymentId)
    {
        private int _sequence;

        public async Task WriteAsync(DeploymentLogLevel level, string message, CancellationToken cancellationToken)
        {
            db.DeploymentLogEntries.Add(new DeploymentLogEntry
            {
                DeploymentId = deploymentId,
                Sequence = ++_sequence,
                Level = level,
                Message = LogSanitizer.Sanitize(message),
            });
            await db.SaveChangesAsync(cancellationToken);
        }
    }
}
