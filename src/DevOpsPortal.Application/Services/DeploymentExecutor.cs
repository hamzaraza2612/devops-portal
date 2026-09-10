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
    ISecretReferenceService secretReferenceService,
    INotificationService notificationService,
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
        await NotifyBestEffortAsync(() => notificationService.NotifyDeploymentStartedAsync(deployment, cancellationToken), "deployment started", deployment.Id);

        try
        {
            var appEnv = deployment.ApplicationEnvironment;
            if (!appEnv.IsActive)
                throw new DeploymentExecutionException("The application-environment configuration is no longer active.");

            if (deployment.Application.DeploymentMode == DeploymentMode.LegacyFilesystem)
            {
                await ExecuteLegacyFilesystemAsync(deployment, appEnv, log, cancellationToken);
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
            await NotifyBestEffortAsync(() => notificationService.NotifyDeploymentOutcomeAsync(deployment, cancellationToken), "deployment succeeded", deployment.Id);
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
            await NotifyBestEffortAsync(() => notificationService.NotifyDeploymentOutcomeAsync(deployment, cancellationToken), "deployment failed", deployment.Id);
        }
    }

    /// <summary>Notification delivery must never break deployment execution —
    /// INotificationService already swallows individual provider failures, but
    /// this extra guard also catches anything unexpected inside the
    /// notification path itself (e.g. a transient DB error resolving
    /// recipients) so it can never be mistaken for a deployment failure.</summary>
    private async Task NotifyBestEffortAsync(Func<Task> notify, string eventDescription, Guid deploymentId)
    {
        try
        {
            await notify();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send '{EventDescription}' notification for deployment {DeploymentId}", eventDescription, deploymentId);
        }
    }

    private async Task ExecuteLegacyFilesystemAsync(Deployment deployment, ApplicationEnvironment appEnv, DeploymentLogWriter log, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(appEnv.DeploymentRootPath))
            throw new DeploymentExecutionException("DeploymentRootPath is not configured.");

        // Re-validate the allow-list here too — config could have changed between
        // request creation and this job actually running.
        var activeRoots = appEnv.TargetServer.AllowedDeploymentRoots.Where(r => r.IsActive).Select(r => r.RootPath);
        if (!DeploymentPathValidator.IsUnderAllowedRoot(appEnv.DeploymentRootPath, activeRoots))
            throw new DeploymentExecutionException("DeploymentRootPath is no longer under an allowed deployment root for its target server.");

        // Resolved only here, at execution time (master requirements §4) — never
        // persisted, never logged by value, and structurally environment-aware
        // (see SecretReferenceService.ResolveForDeploymentAsync): a secret scoped
        // to a different environment is simply not among the candidates below.
        var secretUsername = await db.Users.Where(u => u.Id == deployment.RequestedByUserId).Select(u => u.Username).FirstOrDefaultAsync(cancellationToken);
        var secrets = await secretReferenceService.ResolveForDeploymentAsync(
            deployment.ApplicationId, deployment.EnvironmentDefinitionId, deployment.RequestedByUserId, secretUsername, cancellationToken);
        if (secrets.Count > 0)
        {
            await log.WriteAsync(DeploymentLogLevel.Info,
                $"Resolved {secrets.Count} secret(s) for this deployment: {string.Join(", ", secrets.Keys)}.", cancellationToken);
        }

        var downOperation = appEnv.UseDownWithVolumesOnDeploy ? ComposeOperation.DownWithVolumes : ComposeOperation.Down;
        await log.WriteAsync(DeploymentLogLevel.Info,
            $"Restarting compose stack at '{appEnv.DeploymentRootPath}' ({appEnv.ComposeFilePath}), " +
            $"down={(downOperation == ComposeOperation.DownWithVolumes ? "down -v" : "down")}.", cancellationToken);

        var downResult = await composeExecutor.RunAsync(
            new ComposeCommandRequest(appEnv.DeploymentRootPath, appEnv.ComposeFilePath, appEnv.ComposeProjectName, downOperation, secrets), cancellationToken);
        await log.WriteAsync(
            downResult.Success ? DeploymentLogLevel.Info : DeploymentLogLevel.Warning,
            $"compose down: exit {downResult.ExitCode}\n{Truncate(RedactSecretValues(downResult.StandardOutput, secrets))}\n{Truncate(RedactSecretValues(downResult.StandardError, secrets))}",
            cancellationToken);
        // A failing "down" (e.g. the stack wasn't running yet) is not fatal — proceed to "up".

        var upResult = await composeExecutor.RunAsync(
            new ComposeCommandRequest(appEnv.DeploymentRootPath, appEnv.ComposeFilePath, appEnv.ComposeProjectName, ComposeOperation.Up, secrets), cancellationToken);
        await log.WriteAsync(
            upResult.Success ? DeploymentLogLevel.Info : DeploymentLogLevel.Error,
            $"compose up -d: exit {upResult.ExitCode}\n{Truncate(RedactSecretValues(upResult.StandardOutput, secrets))}\n{Truncate(RedactSecretValues(upResult.StandardError, secrets))}",
            cancellationToken);

        if (!upResult.Success)
            throw new DeploymentExecutionException($"docker compose up failed (exit code {upResult.ExitCode}).");
    }

    /// <summary>Belt-and-braces on top of LogSanitizer's pattern-based redaction
    /// (which only catches KEY=VALUE-shaped or Bearer-token-shaped text): this
    /// replaces every literal occurrence of a resolved secret's actual value,
    /// so a value that leaked into compose output in some other shape still
    /// never reaches DeploymentLogEntry (master requirements §4: "must not
    /// appear in deployment logs").</summary>
    private static string RedactSecretValues(string text, IReadOnlyDictionary<string, string> secrets)
    {
        foreach (var value in secrets.Values)
        {
            if (!string.IsNullOrEmpty(value))
                text = text.Replace(value, "***REDACTED***");
        }
        return text;
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
