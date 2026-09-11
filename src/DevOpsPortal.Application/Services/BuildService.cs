using DevOpsPortal.Application.Abstractions;
using DevOpsPortal.Application.Common;
using DevOpsPortal.Application.Dtos.Builds;
using DevOpsPortal.Application.Exceptions;
using DevOpsPortal.Domain.Constants;
using DevOpsPortal.Domain.Entities;
using DevOpsPortal.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace DevOpsPortal.Application.Services;

/// <summary>
/// Orchestrates build requests: validates the application's BuildConfiguration
/// names a real, configured BuildServer/job, triggers it through whichever
/// IBuildProvider matches that server's ProviderType (never Jenkins-specific
/// code here — master requirements §1), and refreshes status on read rather
/// than via a background poller (same "refresh-on-read" choice Phase 5 made
/// for live container status). Requesting a build never creates a Deployment
/// or otherwise deploys anything (master requirements §3) — a Release row is
/// the only thing a successful build produces, and only IDeploymentService,
/// unchanged by this phase, can turn that into a running deployment.
/// </summary>
public class BuildService(
    IAppDbContext db,
    ICurrentUserService currentUser,
    ICurrentTenantService currentTenantService,
    IAuditService auditService,
    IEnumerable<IBuildProvider> buildProviders) : IBuildService
{
    public async Task<BuildRequestDto> RequestBuildAsync(
        Guid applicationId, RequestBuildRequest request, CancellationToken cancellationToken = default)
    {
        var userId = RequireUserId();
        await EnsurePermissionAsync(userId, PermissionCodes.BuildsRequest, cancellationToken);

        var application = await db.Applications.FirstOrDefaultAsync(a => a.Id == applicationId, cancellationToken)
            ?? throw new NotFoundException("Application", applicationId);
        if (!application.IsActive)
            throw new ValidationException("Application is not active.");

        var buildConfig = await db.BuildConfigurations.FirstOrDefaultAsync(bc => bc.ApplicationId == applicationId, cancellationToken);
        if (buildConfig is null || buildConfig.BuildServerId is null || string.IsNullOrWhiteSpace(buildConfig.JobName))
        {
            throw new ValidationException(
                $"{application.Name} has no build-from-source configuration (BuildServerId/JobName). " +
                "Configure PUT .../build-configuration first, or use the Legacy (prebuilt artifact) deployment path.");
        }

        var buildServer = await db.BuildServers.FirstOrDefaultAsync(bs => bs.Id == buildConfig.BuildServerId, cancellationToken)
            ?? throw new ValidationException($"Configured build server '{buildConfig.BuildServerId}' no longer exists.");
        if (!buildServer.IsActive)
            throw new ValidationException($"Build server '{buildServer.Name}' is not active.");

        if (buildConfig.ImageTagStrategy == ImageTagStrategy.SemVer && string.IsNullOrWhiteSpace(request.SemVer))
            throw new ValidationException($"{application.Name} uses the SemVer image tag strategy — SemVer is required on this request.");

        var buildRequest = new BuildRequest
        {
            TenantId = currentTenantService.RequireTenantId(),
            ApplicationId = applicationId,
            BuildServerId = buildServer.Id,
            JobName = buildConfig.JobName!,
            Branch = NormalizeOrNull(request.Branch),
            CommitSha = NormalizeOrNull(request.CommitSha),
            RequestedSemVer = NormalizeOrNull(request.SemVer),
            RequestedByUserId = userId,
        };

        var provider = buildProviders.FirstOrDefault(p => p.ProviderType == buildServer.ProviderType);
        if (provider is null)
        {
            buildRequest.Status = BuildStatus.Failed;
            buildRequest.ErrorMessage = $"No build provider is registered for type '{buildServer.ProviderType}'.";
            buildRequest.CompletedAt = DateTimeOffset.UtcNow;
        }
        else
        {
            var parameters = BuildParameters(buildConfig, buildRequest);
            var triggerResult = await provider.TriggerBuildAsync(
                buildServer, new BuildTriggerRequest(buildRequest.JobName, buildRequest.Branch, buildRequest.CommitSha, parameters), cancellationToken);

            if (triggerResult.Success)
            {
                buildRequest.Status = BuildStatus.Queued;
                buildRequest.ProviderQueueItemId = triggerResult.ProviderQueueItemId;
            }
            else
            {
                buildRequest.Status = BuildStatus.Failed;
                buildRequest.ErrorMessage = LogSanitizer.Sanitize(triggerResult.ErrorMessage);
                buildRequest.CompletedAt = DateTimeOffset.UtcNow;
            }
        }

        db.BuildRequests.Add(buildRequest);
        await db.SaveChangesAsync(cancellationToken);

        await auditService.LogAsync(
            "build.request", buildRequest.Status == BuildStatus.Failed ? AuditResult.Failure : AuditResult.Success,
            "BuildRequest", buildRequest.Id.ToString(),
            details: $"{application.Name} job '{buildRequest.JobName}' on '{buildServer.Name}'" +
                     (buildRequest.Branch is null ? "" : $" branch '{buildRequest.Branch}'") +
                     (buildRequest.CommitSha is null ? "" : $" commit '{buildRequest.CommitSha}'") +
                     (buildRequest.ErrorMessage is null ? "" : $": {buildRequest.ErrorMessage}"),
            cancellationToken: cancellationToken);

        return await ToDtoAsync(buildRequest, cancellationToken);
    }

    public async Task<BuildRequestDto> GetAsync(Guid buildRequestId, CancellationToken cancellationToken = default)
    {
        var userId = RequireUserId();
        await EnsurePermissionAsync(userId, PermissionCodes.BuildsView, cancellationToken);

        var buildRequest = await db.BuildRequests
            .Include(br => br.BuildServer)
            .Include(br => br.Application)
            .FirstOrDefaultAsync(br => br.Id == buildRequestId, cancellationToken)
            ?? throw new NotFoundException("BuildRequest", buildRequestId);

        if (buildRequest.Status is BuildStatus.Queued or BuildStatus.Running)
            await RefreshStatusAsync(buildRequest, cancellationToken);

        return await ToDtoAsync(buildRequest, cancellationToken);
    }

    public async Task<IReadOnlyList<BuildRequestDto>> ListAsync(Guid? applicationId, CancellationToken cancellationToken = default)
    {
        var userId = RequireUserId();
        await EnsurePermissionAsync(userId, PermissionCodes.BuildsView, cancellationToken);

        var query = db.BuildRequests.AsQueryable();
        if (applicationId is not null)
            query = query.Where(br => br.ApplicationId == applicationId);

        var buildRequests = await query.OrderByDescending(br => br.RequestedAt).ToListAsync(cancellationToken);
        var result = new List<BuildRequestDto>(buildRequests.Count);
        foreach (var br in buildRequests)
            result.Add(await ToDtoAsync(br, cancellationToken));
        return result;
    }

    public async Task<BuildLogDto> GetLogAsync(Guid buildRequestId, CancellationToken cancellationToken = default)
    {
        var userId = RequireUserId();
        await EnsurePermissionAsync(userId, PermissionCodes.BuildsView, cancellationToken);

        var buildRequest = await db.BuildRequests
            .Include(br => br.BuildServer)
            .FirstOrDefaultAsync(br => br.Id == buildRequestId, cancellationToken)
            ?? throw new NotFoundException("BuildRequest", buildRequestId);

        if (buildRequest.BuildNumber is null)
            return new BuildLogDto(false, null, false, "Build number is not known yet — the build is still queued.");

        var provider = buildProviders.FirstOrDefault(p => p.ProviderType == buildRequest.BuildServer.ProviderType);
        if (provider is null)
            return new BuildLogDto(false, null, false, $"No build provider is registered for type '{buildRequest.BuildServer.ProviderType}'.");

        var result = await provider.GetBuildLogAsync(buildRequest.BuildServer, buildRequest.JobName, buildRequest.BuildNumber.Value, cancellationToken);
        return result.Success
            ? new BuildLogDto(true, LogSanitizer.Sanitize(result.LogText), result.IsComplete, null)
            : new BuildLogDto(false, null, false, result.ErrorMessage);
    }

    // ------------------------------------------------------------- internals

    private async Task RefreshStatusAsync(BuildRequest buildRequest, CancellationToken cancellationToken)
    {
        var provider = buildProviders.FirstOrDefault(p => p.ProviderType == buildRequest.BuildServer.ProviderType);
        if (provider is null)
            return;

        var result = await provider.GetBuildStatusAsync(
            buildRequest.BuildServer, buildRequest.JobName, buildRequest.ProviderQueueItemId, buildRequest.BuildNumber, cancellationToken);
        if (!result.Success || result.State is null)
            return;

        buildRequest.BuildNumber ??= result.BuildNumber;
        if (result.BuildUrl is not null)
            buildRequest.BuildUrl = result.BuildUrl;

        var newStatus = result.State switch
        {
            ProviderBuildLifecycleState.Queued => BuildStatus.Queued,
            ProviderBuildLifecycleState.Running => BuildStatus.Running,
            ProviderBuildLifecycleState.Succeeded => BuildStatus.Succeeded,
            ProviderBuildLifecycleState.Failed => BuildStatus.Failed,
            _ => buildRequest.Status,
        };

        if (newStatus == BuildStatus.Running && buildRequest.StartedAt is null)
            buildRequest.StartedAt = DateTimeOffset.UtcNow;

        var wasNotTerminal = buildRequest.Status is not (BuildStatus.Succeeded or BuildStatus.Failed);
        buildRequest.Status = newStatus;

        if (newStatus is BuildStatus.Succeeded or BuildStatus.Failed)
        {
            if (buildRequest.CompletedAt is null)
                buildRequest.CompletedAt = DateTimeOffset.UtcNow;

            if (newStatus == BuildStatus.Failed && buildRequest.ErrorMessage is null)
                buildRequest.ErrorMessage = "The build finished with a non-success result.";

            if (newStatus == BuildStatus.Succeeded && wasNotTerminal)
                await CreateReleaseAsync(buildRequest, cancellationToken);
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task CreateReleaseAsync(BuildRequest buildRequest, CancellationToken cancellationToken)
    {
        var alreadyExists = await db.Releases.AnyAsync(r => r.BuildRequestId == buildRequest.Id, cancellationToken);
        if (alreadyExists || buildRequest.BuildNumber is null)
            return;

        var buildConfig = await db.BuildConfigurations.FirstOrDefaultAsync(bc => bc.ApplicationId == buildRequest.ApplicationId, cancellationToken);

        var tag = (buildConfig?.ImageTagStrategy) switch
        {
            ImageTagStrategy.CommitSha when !string.IsNullOrWhiteSpace(buildRequest.CommitSha) => buildRequest.CommitSha,
            ImageTagStrategy.SemVer when !string.IsNullOrWhiteSpace(buildRequest.RequestedSemVer) => buildRequest.RequestedSemVer,
            _ => buildRequest.BuildNumber.Value.ToString(),
        };

        var imageName = buildConfig?.ImageRegistry is { Length: > 0 } registry
            ? $"{registry}/{buildConfig.ImageRepository}"
            : buildConfig?.ImageRepository ?? buildRequest.Application.Slug;

        var release = new Release
        {
            TenantId = buildRequest.TenantId,
            ApplicationId = buildRequest.ApplicationId,
            BuildRequestId = buildRequest.Id,
            CommitSha = buildRequest.CommitSha ?? string.Empty,
            Branch = buildRequest.Branch,
            BuildNumber = buildRequest.BuildNumber.Value,
            ImageReference = $"{imageName}:{tag}",
            BuildStatus = BuildStatus.Succeeded,
        };
        db.Releases.Add(release);
    }

    private static Dictionary<string, string> BuildParameters(BuildConfiguration buildConfig, BuildRequest buildRequest)
    {
        var parameters = new Dictionary<string, string>();
        if (buildConfig.ProjectOrSolutionPath is not null) parameters["PROJECT_PATH"] = buildConfig.ProjectOrSolutionPath;
        if (buildConfig.PublishConfiguration is not null) parameters["PUBLISH_CONFIGURATION"] = buildConfig.PublishConfiguration;
        if (buildConfig.PublishArguments is not null) parameters["PUBLISH_ARGUMENTS"] = buildConfig.PublishArguments;
        if (buildConfig.SdkVersion is not null) parameters["SDK_VERSION"] = buildConfig.SdkVersion;
        if (buildConfig.DockerfilePath is not null) parameters["DOCKERFILE_PATH"] = buildConfig.DockerfilePath;
        if (buildConfig.ImageRegistry is not null) parameters["IMAGE_REGISTRY"] = buildConfig.ImageRegistry;
        if (buildConfig.ImageRepository is not null) parameters["IMAGE_REPOSITORY"] = buildConfig.ImageRepository;
        if (buildRequest.CommitSha is not null) parameters["COMMIT_SHA"] = buildRequest.CommitSha;
        if (buildRequest.Branch is not null) parameters["BRANCH"] = buildRequest.Branch;
        return parameters;
    }

    private async Task<BuildRequestDto> ToDtoAsync(BuildRequest br, CancellationToken cancellationToken)
    {
        var applicationName = br.Application?.Name
            ?? await db.Applications.Where(a => a.Id == br.ApplicationId).Select(a => a.Name).FirstOrDefaultAsync(cancellationToken)
            ?? string.Empty;
        var buildServer = br.BuildServer
            ?? await db.BuildServers.FirstOrDefaultAsync(bs => bs.Id == br.BuildServerId, cancellationToken);
        var requestedByUsername = await db.Users.Where(u => u.Id == br.RequestedByUserId).Select(u => u.Username).FirstOrDefaultAsync(cancellationToken);
        var release = await db.Releases.FirstOrDefaultAsync(r => r.BuildRequestId == br.Id, cancellationToken);

        return new BuildRequestDto(
            br.Id, br.ApplicationId, applicationName,
            br.BuildServerId, buildServer?.Name ?? string.Empty, buildServer?.ProviderType ?? default, br.JobName,
            br.Branch, br.CommitSha,
            br.Status, br.ProviderQueueItemId, br.BuildNumber, br.BuildUrl,
            br.ErrorMessage, br.RequestedByUserId, requestedByUsername, br.RequestedAt,
            br.StartedAt, br.CompletedAt,
            release?.Id, release?.ImageReference);
    }

    private static string? NormalizeOrNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private Guid RequireUserId() => currentUser.UserId ?? throw new ForbiddenException("Not authenticated.");

    private async Task EnsurePermissionAsync(Guid userId, string permissionCode, CancellationToken cancellationToken)
    {
        var (_, permissions) = await db.GetRolesAndPermissionsAsync(userId, cancellationToken);
        if (!permissions.Contains(permissionCode))
            throw new ForbiddenException($"Missing required permission '{permissionCode}'.");
    }
}
