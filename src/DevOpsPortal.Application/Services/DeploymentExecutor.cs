using DevOpsPortal.Application.Abstractions;
using DevOpsPortal.Application.Common;
using DevOpsPortal.Application.Exceptions;
using DevOpsPortal.Domain.Entities;
using DevOpsPortal.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace DevOpsPortal.Application.Services;

/// <summary>
/// Runs a single queued Deployment: re-validates server-side (config can have
/// changed since the request was created), executes the configured
/// operations, health-checks, and finalizes status. Every step is logged
/// (sanitized) to DeploymentLogEntry. Never invoked from an HTTP request —
/// only from DeploymentWorker in its own DI scope.
///
/// <para>LegacyFilesystem execution runs `docker compose` on the deployment's
/// configured TargetServer via <see cref="IRemoteExecutionProvider"/> — never
/// locally (master requirements §5/§14: the portal VM is separate from every
/// target server). Resolved secrets are passed through as
/// ComposeCommandRequest.EnvironmentVariables, exactly as the local-execution
/// path did before Phase 12; only how that command actually reaches the
/// target server changed.</para>
/// </summary>
public class DeploymentExecutor(
    IAppDbContext db,
    IRemoteExecutionProvider remoteExecutionProvider,
    IHealthCheckProbe healthCheckProbe,
    IAuditService auditService,
    ISecretReferenceService secretReferenceService,
    INotificationService notificationService,
    IConfiguration configuration,
    ILogger<DeploymentExecutor> logger) : IDeploymentExecutor
{
    // The deployment queue is processed one job at a time (DeploymentWorker), so a
    // single stuck `docker compose up` (e.g. an image pull that never completes) would
    // otherwise stall every subsequent deployment indefinitely. Configurable via
    // Deployment:ExecutionTimeoutMinutes / DEPLOYMENT_EXECUTION_TIMEOUT_MINUTES (a
    // double, so tests can configure a sub-second value; operators only ever set whole
    // minutes).
    private double TimeoutMinutes => double.TryParse(configuration["Deployment:ExecutionTimeoutMinutes"], out var minutes) && minutes > 0 ? minutes : 20;

    /// <summary>The env var name a ContainerImage-mode compose file must reference
    /// (e.g. `image: ${IMAGE_REFERENCE}`) to receive the deployment's resolved
    /// Release.ImageReference — see ExecuteContainerImageAsync.</summary>
    private const string ImageReferenceEnvVarName = "IMAGE_REFERENCE";

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

        // Bounds the whole execution (compose commands + health check) so one stuck job
        // can never block the single-threaded queue forever; does not affect the
        // cancellationToken used for the final status write below, which must still be
        // able to persist a Failed/timed-out outcome.
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromMinutes(TimeoutMinutes));
        var executionToken = timeoutCts.Token;

        try
        {
            var appEnv = deployment.ApplicationEnvironment;
            if (!appEnv.IsActive)
                throw new DeploymentExecutionException("The application-environment configuration is no longer active.");

            if (deployment.Application.DeploymentMode == DeploymentMode.LegacyFilesystem)
            {
                await ExecuteLegacyFilesystemAsync(deployment, appEnv, log, executionToken);
            }
            else
            {
                await ExecuteContainerImageAsync(deployment, appEnv, log, executionToken);
            }

            await log.WriteAsync(DeploymentLogLevel.Info, "Running post-deployment health check.", cancellationToken);
            var healthResult = await healthCheckProbe.ProbeAsync(
                appEnv.HealthCheckType, appEnv.HealthCheckEndpoint, appEnv.HealthCheckTimeoutSeconds, executionToken);

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
            var timedOut = ex is OperationCanceledException && timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested;
            var reason = timedOut ? $"Deployment timed out after {TimeoutMinutes} minute(s)." : ex.Message;

            deployment.Status = DeploymentStatus.Failed;
            deployment.CompletedAt = DateTimeOffset.UtcNow;
            deployment.FailureReason = LogSanitizer.Sanitize(reason);
            await log.WriteAsync(DeploymentLogLevel.Error, $"Deployment failed: {reason}", cancellationToken);
            await db.SaveChangesAsync(cancellationToken);

            await auditService.LogAsync(deployment.IsRollback ? "rollback.failed" : "deployment.failed", AuditResult.Failure,
                "Deployment", deployment.Id.ToString(),
                details: $"{deployment.Application.Name}/{deployment.EnvironmentDefinition.Name} commit {deployment.CommitSha}: {LogSanitizer.Sanitize(reason)}",
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

        if (!remoteExecutionProvider.IsConfigured(appEnv.TargetServer))
        {
            throw new DeploymentExecutionException(
                $"Target server '{appEnv.TargetServer.Name}' has no SSH connection configured (Hostname, SshUsername, and a stored " +
                "credential are all required) — see the Servers page to configure and test connectivity before deploying.");
        }

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

        var downResult = await remoteExecutionProvider.RunComposeAsync(
            appEnv.TargetServer,
            new ComposeCommandRequest(appEnv.DeploymentRootPath, appEnv.ComposeFilePath, appEnv.ComposeProjectName, downOperation, secrets), cancellationToken);
        await log.WriteAsync(
            downResult.Success ? DeploymentLogLevel.Info : DeploymentLogLevel.Warning,
            $"compose down: exit {downResult.ExitCode}\n{Truncate(RedactSecretValues(downResult.StandardOutput, secrets))}\n{Truncate(RedactSecretValues(downResult.StandardError, secrets))}",
            cancellationToken);
        // A failing "down" (e.g. the stack wasn't running yet) is not fatal — proceed to "up".

        var upResult = await remoteExecutionProvider.RunComposeAsync(
            appEnv.TargetServer,
            new ComposeCommandRequest(appEnv.DeploymentRootPath, appEnv.ComposeFilePath, appEnv.ComposeProjectName, ComposeOperation.Up, secrets), cancellationToken);
        await log.WriteAsync(
            upResult.Success ? DeploymentLogLevel.Info : DeploymentLogLevel.Error,
            $"compose up -d: exit {upResult.ExitCode}\n{Truncate(RedactSecretValues(upResult.StandardOutput, secrets))}\n{Truncate(RedactSecretValues(upResult.StandardError, secrets))}",
            cancellationToken);

        if (!upResult.Success)
            throw new DeploymentExecutionException($"docker compose up failed (exit code {upResult.ExitCode}).");
    }

    /// <summary>ContainerImage-mode deployment (master requirements §16/§17):
    /// pulls the deployment's immutable Release.ImageReference on the target
    /// server, then recreates the compose stack from it. The same
    /// DeploymentRootPath/ComposeFilePath/ComposeProjectName ApplicationEnvironment
    /// config LegacyFilesystem uses already points at a docker-compose.yml on the
    /// target server — for ContainerImage mode that file is expected to reference
    /// the image via <c>${IMAGE_REFERENCE}</c> interpolation (e.g. <c>image:
    /// ${IMAGE_REFERENCE}</c>), which is passed through exactly like a resolved
    /// secret, never written to any file, never logged by value. Registry
    /// authentication on the target server (`docker login`, or a registry
    /// credential helper) is expected to already be configured out-of-band — no
    /// registry-credential entity exists in this phase (see PROJECT_STATE.md).</summary>
    private async Task ExecuteContainerImageAsync(Deployment deployment, ApplicationEnvironment appEnv, DeploymentLogWriter log, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(deployment.ImageReference))
            throw new DeploymentExecutionException("This deployment has no ImageReference — ContainerImage deployments must be created from a Release.");

        if (string.IsNullOrWhiteSpace(appEnv.DeploymentRootPath))
            throw new DeploymentExecutionException("DeploymentRootPath is not configured.");

        var activeRoots = appEnv.TargetServer.AllowedDeploymentRoots.Where(r => r.IsActive).Select(r => r.RootPath);
        if (!DeploymentPathValidator.IsUnderAllowedRoot(appEnv.DeploymentRootPath, activeRoots))
            throw new DeploymentExecutionException("DeploymentRootPath is no longer under an allowed deployment root for its target server.");

        if (!remoteExecutionProvider.IsConfigured(appEnv.TargetServer))
        {
            throw new DeploymentExecutionException(
                $"Target server '{appEnv.TargetServer.Name}' has no SSH connection configured (Hostname, SshUsername, and a stored " +
                "credential are all required) — see the Servers page to configure and test connectivity before deploying.");
        }

        var secretUsername = await db.Users.Where(u => u.Id == deployment.RequestedByUserId).Select(u => u.Username).FirstOrDefaultAsync(cancellationToken);
        var secrets = await secretReferenceService.ResolveForDeploymentAsync(
            deployment.ApplicationId, deployment.EnvironmentDefinitionId, deployment.RequestedByUserId, secretUsername, cancellationToken);

        var envVars = new Dictionary<string, string>(secrets) { [ImageReferenceEnvVarName] = deployment.ImageReference };
        await log.WriteAsync(DeploymentLogLevel.Info, $"Deploying image '{deployment.ImageReference}' to '{appEnv.DeploymentRootPath}' ({appEnv.ComposeFilePath}).", cancellationToken);

        var pullResult = await remoteExecutionProvider.RunComposeAsync(
            appEnv.TargetServer,
            new ComposeCommandRequest(appEnv.DeploymentRootPath, appEnv.ComposeFilePath, appEnv.ComposeProjectName, ComposeOperation.Pull, envVars), cancellationToken);
        await log.WriteAsync(
            pullResult.Success ? DeploymentLogLevel.Info : DeploymentLogLevel.Error,
            $"compose pull: exit {pullResult.ExitCode}\n{Truncate(RedactSecretValues(pullResult.StandardOutput, secrets))}\n{Truncate(RedactSecretValues(pullResult.StandardError, secrets))}",
            cancellationToken);

        if (!pullResult.Success)
        {
            throw new DeploymentExecutionException(
                $"docker compose pull failed (exit code {pullResult.ExitCode}) — the image '{deployment.ImageReference}' may not exist in the registry, " +
                "or the target server is not authenticated to pull it.");
        }

        var upResult = await remoteExecutionProvider.RunComposeAsync(
            appEnv.TargetServer,
            new ComposeCommandRequest(appEnv.DeploymentRootPath, appEnv.ComposeFilePath, appEnv.ComposeProjectName, ComposeOperation.Up, envVars), cancellationToken);
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
