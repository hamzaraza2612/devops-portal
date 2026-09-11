using DevOpsPortal.Application.Abstractions;
using DevOpsPortal.Application.Common;
using DevOpsPortal.Application.Dtos.Containers;
using DevOpsPortal.Application.Exceptions;
using DevOpsPortal.Domain.Constants;
using DevOpsPortal.Domain.Entities;
using DevOpsPortal.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace DevOpsPortal.Application.Services;

/// <summary>
/// Every container status/control operation resolves its target exclusively
/// through the ApplicationEnvironment's own configured TargetServer, and reaches
/// it only via IContainerRuntimeProvider — this service has no other path to
/// Docker, and never assumes the portal process itself can see any target
/// server's containers (see PROJECT_STATE.md's Phase 5 remote-execution
/// correction). With only NotConfiguredRemoteExecutionProvider registered
/// today, every call below honestly reports the target server as unreachable;
/// the permission/validation/audit logic is real and ready for when a real
/// provider is plugged in.
/// </summary>
public class ContainerOperationsService(
    IAppDbContext db,
    ICurrentUserService currentUser,
    IAuditService auditService,
    IContainerRuntimeProvider containerRuntimeProvider,
    IHealthCheckProbe healthCheckProbe) : IContainerOperationsService
{
    public async Task<ContainerEnvironmentStatusDto> GetStatusAsync(
        Guid applicationId, Guid environmentDefinitionId, CancellationToken cancellationToken = default)
    {
        var userId = RequireUserId();
        await EnsurePermissionAsync(userId, PermissionCodes.ContainersView, cancellationToken);
        await EnsureEnvironmentAccessAsync(userId, environmentDefinitionId, cancellationToken);

        var (application, environmentDefinition, appEnv) = await LoadAsync(applicationId, environmentDefinitionId, cancellationToken);

        var latestDeployment = await db.Deployments
            .Where(d => d.ApplicationId == applicationId && d.EnvironmentDefinitionId == environmentDefinitionId)
            .OrderByDescending(d => d.RequestedAt)
            .Select(d => new { d.Id, d.Status })
            .FirstOrDefaultAsync(cancellationToken);

        if (appEnv is null || application.DeploymentMode != DeploymentMode.LegacyFilesystem ||
            string.IsNullOrWhiteSpace(appEnv.DeploymentRootPath))
        {
            return new ContainerEnvironmentStatusDto(
                applicationId, environmentDefinitionId, environmentDefinition.Name,
                IsConfigured: false, IsReachable: false, UnreachableReason: null, TargetServerName: appEnv?.TargetServer.Name,
                ExpectedServiceName: appEnv?.ServiceName, ExpectedContainerName: appEnv?.ContainerName,
                Containers: [], HealthCheck: null, CurrentImageOrVersion: null, LastRestartAt: null,
                latestDeployment?.Id, latestDeployment?.Status);
        }

        var runtimeStatus = await containerRuntimeProvider.GetStatusAsync(
            appEnv.TargetServer, appEnv.DeploymentRootPath, appEnv.ComposeFilePath, appEnv.ComposeProjectName, cancellationToken);
        var filtered = string.IsNullOrWhiteSpace(appEnv.ServiceName)
            ? runtimeStatus.Containers
            : runtimeStatus.Containers.Where(c => c.ServiceName == appEnv.ServiceName).ToList();
        var containers = filtered.Select(ToDto).ToList();

        var healthCheck = await BuildHealthCheckStatusAsync(applicationId, environmentDefinitionId, appEnv, cancellationToken);

        var primary = containers.FirstOrDefault();
        var currentImageOrVersion = primary is not null
            ? primary.ImageTag is null ? primary.Image : $"{primary.Image}:{primary.ImageTag}"
            : null;
        var lastRestartAt = containers.Where(c => c.StartedAt is not null).Select(c => c.StartedAt).OrderDescending().FirstOrDefault();

        return new ContainerEnvironmentStatusDto(
            applicationId, environmentDefinitionId, environmentDefinition.Name,
            IsConfigured: true, runtimeStatus.IsReachable, runtimeStatus.UnreachableReason, appEnv.TargetServer.Name,
            appEnv.ServiceName, appEnv.ContainerName,
            containers, healthCheck, currentImageOrVersion, lastRestartAt,
            latestDeployment?.Id, latestDeployment?.Status);
    }

    public Task<ContainerActionResultDto> RestartAsync(Guid applicationId, Guid environmentDefinitionId, CancellationToken cancellationToken = default) =>
        RunControlOperationAsync(applicationId, environmentDefinitionId, ComposeOperation.Restart, "container.restart", "restart", cancellationToken);

    public Task<ContainerActionResultDto> StartAsync(Guid applicationId, Guid environmentDefinitionId, CancellationToken cancellationToken = default) =>
        RunControlOperationAsync(applicationId, environmentDefinitionId, ComposeOperation.Start, "container.start", "start", cancellationToken);

    public Task<ContainerActionResultDto> StopAsync(Guid applicationId, Guid environmentDefinitionId, CancellationToken cancellationToken = default) =>
        RunControlOperationAsync(applicationId, environmentDefinitionId, ComposeOperation.Stop, "container.stop", "stop", cancellationToken);

    private async Task<ContainerActionResultDto> RunControlOperationAsync(
        Guid applicationId, Guid environmentDefinitionId, ComposeOperation operation, string auditAction, string verb, CancellationToken cancellationToken)
    {
        var userId = RequireUserId();
        await EnsurePermissionAsync(userId, PermissionCodes.ContainersControl, cancellationToken);
        await EnsureEnvironmentAccessAsync(userId, environmentDefinitionId, cancellationToken);

        var (application, environmentDefinition, appEnv) = await LoadAsync(applicationId, environmentDefinitionId, cancellationToken);
        RequireConfigured(application, appEnv);

        var result = await containerRuntimeProvider.RunOperationAsync(
            appEnv!.TargetServer, appEnv.DeploymentRootPath!, appEnv.ComposeFilePath, appEnv.ComposeProjectName, operation, cancellationToken);

        var sanitizedDetail = LogSanitizer.Sanitize(result.Message);
        await auditService.LogAsync(
            auditAction, result.IsReachable && result.Success ? AuditResult.Success : AuditResult.Failure, "ApplicationEnvironment", appEnv.Id.ToString(),
            details: $"{application.Name}/{environmentDefinition.Name} (target server '{appEnv.TargetServer.Name}'): {sanitizedDetail}",
            cancellationToken: cancellationToken);

        if (!result.IsReachable)
            return new ContainerActionResultDto(false, sanitizedDetail, DateTimeOffset.UtcNow);

        return new ContainerActionResultDto(
            result.Success,
            result.Success ? $"Containers for {application.Name}/{environmentDefinition.Name} were sent a {verb}." : $"Failed to {verb} containers: {sanitizedDetail}",
            DateTimeOffset.UtcNow);
    }

    public async Task<ContainerActionResultDto> RecreateWithVolumesAsync(
        Guid applicationId, Guid environmentDefinitionId, RecreateWithVolumesRequest request, CancellationToken cancellationToken = default)
    {
        var userId = RequireUserId();
        await EnsurePermissionAsync(userId, PermissionCodes.ContainersRecreate, cancellationToken);
        await EnsureEnvironmentAccessAsync(userId, environmentDefinitionId, cancellationToken);

        var (application, environmentDefinition, appEnv) = await LoadAsync(applicationId, environmentDefinitionId, cancellationToken);
        RequireConfigured(application, appEnv);

        if (!request.Confirm)
            throw new ValidationException("This action destroys volumes for this application environment and must be explicitly confirmed (Confirm: true).");

        if (!appEnv!.UseDownWithVolumesOnDeploy)
        {
            throw new ValidationException(
                $"{application.Name}/{environmentDefinition.Name} is not configured to allow volume-destroying operations. " +
                "Enable UseDownWithVolumesOnDeploy for this application environment first.");
        }

        var hasActiveDeployment = await db.Deployments.AnyAsync(d =>
            d.ApplicationId == applicationId && d.EnvironmentDefinitionId == environmentDefinitionId &&
            (d.Status == DeploymentStatus.Pending || d.Status == DeploymentStatus.Queued || d.Status == DeploymentStatus.Running),
            cancellationToken);
        if (hasActiveDeployment)
            throw new ConflictException("A deployment is currently in progress for this application environment; wait for it to finish before recreating containers.");

        await auditService.LogAsync(
            "container.recreate.requested", AuditResult.Success, "ApplicationEnvironment", appEnv.Id.ToString(),
            details: $"{application.Name}/{environmentDefinition.Name} (target server '{appEnv.TargetServer.Name}'): docker compose down -v / up -d requested (destroys volumes).",
            cancellationToken: cancellationToken);

        var downResult = await containerRuntimeProvider.RunOperationAsync(
            appEnv.TargetServer, appEnv.DeploymentRootPath!, appEnv.ComposeFilePath, appEnv.ComposeProjectName, ComposeOperation.DownWithVolumes, cancellationToken);

        if (!downResult.IsReachable)
        {
            var unreachableDetail = LogSanitizer.Sanitize(downResult.Message);
            await auditService.LogAsync(
                "container.recreate.failed", AuditResult.Failure, "ApplicationEnvironment", appEnv.Id.ToString(),
                details: $"{application.Name}/{environmentDefinition.Name} (target server '{appEnv.TargetServer.Name}'): {unreachableDetail}",
                cancellationToken: cancellationToken);
            return new ContainerActionResultDto(false, unreachableDetail, DateTimeOffset.UtcNow);
        }
        // A failing "down -v" (e.g. the stack wasn't running yet) is not fatal — proceed to "up",
        // matching DeploymentExecutor's existing tolerance for the same sequence during a deploy.

        var upResult = await containerRuntimeProvider.RunOperationAsync(
            appEnv.TargetServer, appEnv.DeploymentRootPath!, appEnv.ComposeFilePath, appEnv.ComposeProjectName, ComposeOperation.Up, cancellationToken);

        var sanitizedDetail = LogSanitizer.Sanitize($"down -v: {downResult.Message}; up -d: {upResult.Message}");

        await auditService.LogAsync(
            upResult.IsReachable && upResult.Success ? "container.recreate.succeeded" : "container.recreate.failed",
            upResult.IsReachable && upResult.Success ? AuditResult.Success : AuditResult.Failure, "ApplicationEnvironment", appEnv.Id.ToString(),
            details: $"{application.Name}/{environmentDefinition.Name} (target server '{appEnv.TargetServer.Name}'): {sanitizedDetail}", cancellationToken: cancellationToken);

        if (!upResult.IsReachable)
            return new ContainerActionResultDto(false, LogSanitizer.Sanitize(upResult.Message), DateTimeOffset.UtcNow);

        return new ContainerActionResultDto(
            upResult.Success,
            upResult.Success
                ? $"{application.Name}/{environmentDefinition.Name} was recreated (volumes destroyed)."
                : $"Recreate failed: {sanitizedDetail}",
            DateTimeOffset.UtcNow);
    }

    private static ContainerInfoDto ToDto(ContainerStatusInfo info) => new(
        info.ServiceName, info.ContainerName, info.Image, info.ImageTag, info.State, info.DockerHealthStatus,
        info.StartedAt,
        info.State == ContainerState.Running && info.StartedAt is { } startedAt ? DateTimeOffset.UtcNow - startedAt : null,
        info.RestartCount, info.Ports);

    private async Task<HealthCheckStatusDto> BuildHealthCheckStatusAsync(
        Guid applicationId, Guid environmentDefinitionId, ApplicationEnvironment appEnv, CancellationToken cancellationToken)
    {
        var lastSuccessfulCheckAt = await db.Deployments
            .Where(d => d.ApplicationId == applicationId && d.EnvironmentDefinitionId == environmentDefinitionId && d.HealthCheckPassed == true)
            .OrderByDescending(d => d.CompletedAt)
            .Select(d => d.CompletedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (appEnv.HealthCheckType == HealthCheckType.None)
            return new HealthCheckStatusDto(HealthCheckType.None, null, null, DateTimeOffset.UtcNow, lastSuccessfulCheckAt);

        // Unlike Docker container inspection, this is a plain HTTP/TCP call to
        // ApplicationEnvironment.HealthCheckEndpoint — Phase 3 already requires
        // that endpoint to be "independently reachable from wherever the
        // deployment engine runs" (i.e. over the network, not via a local
        // socket), so it is unaffected by the portal/TargetServer separation
        // that IContainerRuntimeProvider exists to handle.
        var probe = await healthCheckProbe.ProbeAsync(appEnv.HealthCheckType, appEnv.HealthCheckEndpoint, appEnv.HealthCheckTimeoutSeconds, cancellationToken);
        return new HealthCheckStatusDto(appEnv.HealthCheckType, probe.Passed, LogSanitizer.Sanitize(probe.Detail), DateTimeOffset.UtcNow, lastSuccessfulCheckAt);
    }

    private async Task<(ManagedApplication Application, EnvironmentDefinition EnvironmentDefinition, ApplicationEnvironment? AppEnv)> LoadAsync(
        Guid applicationId, Guid environmentDefinitionId, CancellationToken cancellationToken)
    {
        var application = await db.Applications.FirstOrDefaultAsync(a => a.Id == applicationId, cancellationToken)
            ?? throw new NotFoundException("Application", applicationId);

        var environmentDefinition = await db.EnvironmentDefinitions.FirstOrDefaultAsync(e => e.Id == environmentDefinitionId, cancellationToken)
            ?? throw new NotFoundException("EnvironmentDefinition", environmentDefinitionId);

        var appEnv = await db.ApplicationEnvironments
            .Include(ae => ae.TargetServer)
            .FirstOrDefaultAsync(ae => ae.ApplicationId == applicationId && ae.EnvironmentDefinitionId == environmentDefinitionId && ae.IsActive, cancellationToken);

        return (application, environmentDefinition, appEnv);
    }

    private static void RequireConfigured(ManagedApplication application, ApplicationEnvironment? appEnv)
    {
        if (appEnv is null || string.IsNullOrWhiteSpace(appEnv.DeploymentRootPath))
            throw new ValidationException($"{application.Name} has no active, configured environment to operate on.");
        if (application.DeploymentMode != DeploymentMode.LegacyFilesystem)
            throw new ValidationException("Container operations are only available for LegacyFilesystem-mode applications in this phase.");
    }

    private Guid RequireUserId() => currentUser.UserId ?? throw new ForbiddenException("Not authenticated.");

    private async Task EnsurePermissionAsync(Guid userId, string permissionCode, CancellationToken cancellationToken)
    {
        var (_, permissions) = await db.GetRolesAndPermissionsAsync(userId, cancellationToken);
        if (!permissions.Contains(permissionCode))
            throw new ForbiddenException($"Missing required permission '{permissionCode}'.");
    }

    /// <summary>ContainersView/Control/Recreate are granted to anyone with access
    /// to ANY environment (see AppDbContextExtensions) — this confirms the user
    /// is specifically allowed to act on THIS environment (master requirements
    /// §2/§12).</summary>
    private async Task EnsureEnvironmentAccessAsync(Guid userId, Guid environmentDefinitionId, CancellationToken cancellationToken)
    {
        if (!await db.HasEnvironmentAccessAsync(userId, environmentDefinitionId, cancellationToken))
            throw new ForbiddenException("You do not have access to this environment.");
    }
}
