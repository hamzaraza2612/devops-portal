using DevOpsPortal.Application.Abstractions;
using DevOpsPortal.Application.Common;
using DevOpsPortal.Application.Dtos.Applications;
using DevOpsPortal.Application.Exceptions;
using DevOpsPortal.Domain.Entities;
using DevOpsPortal.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace DevOpsPortal.Application.Services;

public class ApplicationEnvironmentService(IAppDbContext db, IAuditService auditService, IGitProviderClient gitProviderClient)
    : IApplicationEnvironmentService
{
    public async Task<IReadOnlyList<ApplicationEnvironmentDto>> GetForApplicationAsync(
        Guid applicationId, CancellationToken cancellationToken = default)
    {
        if (!await db.Applications.AnyAsync(a => a.Id == applicationId, cancellationToken))
            throw new NotFoundException("Application", applicationId);

        var rows = await db.ApplicationEnvironments
            .Include(ae => ae.EnvironmentDefinition)
            .Include(ae => ae.TargetServer)
            .Where(ae => ae.ApplicationId == applicationId)
            .OrderBy(ae => ae.EnvironmentDefinition.SortOrder)
            .ToListAsync(cancellationToken);

        return rows.Select(ToDto).ToList();
    }

    public async Task<ApplicationEnvironmentDto> GetAsync(
        Guid applicationId, Guid environmentDefinitionId, CancellationToken cancellationToken = default)
    {
        var row = await LoadAsync(applicationId, environmentDefinitionId, cancellationToken)
            ?? throw new NotFoundException("ApplicationEnvironment", $"{applicationId}/{environmentDefinitionId}");
        return ToDto(row);
    }

    public async Task<ApplicationEnvironmentDto> UpsertAsync(
        Guid applicationId, Guid environmentDefinitionId, UpsertApplicationEnvironmentRequest request, CancellationToken cancellationToken = default)
    {
        var application = await db.Applications.FirstOrDefaultAsync(a => a.Id == applicationId, cancellationToken)
            ?? throw new NotFoundException("Application", applicationId);

        var environmentDefinition = await db.EnvironmentDefinitions
            .FirstOrDefaultAsync(e => e.Id == environmentDefinitionId, cancellationToken)
            ?? throw new NotFoundException("EnvironmentDefinition", environmentDefinitionId);

        var targetServer = await db.TargetServers
            .Include(s => s.AllowedDeploymentRoots)
            .FirstOrDefaultAsync(s => s.Id == request.TargetServerId, cancellationToken);
        if (targetServer is null || !targetServer.IsActive)
            throw new ValidationException("TargetServerId does not refer to an active target server.");

        ValidateForMode(application.DeploymentMode, request, targetServer);

        var row = await LoadAsync(applicationId, environmentDefinitionId, cancellationToken);
        var isNew = row is null;
        row ??= new ApplicationEnvironment { ApplicationId = applicationId, EnvironmentDefinitionId = environmentDefinitionId };

        row.TargetServerId = request.TargetServerId;
        row.BranchName = string.IsNullOrWhiteSpace(request.BranchName) ? null : request.BranchName.Trim();
        row.DeploymentRootPath = NormalizedOrNull(request.DeploymentRootPath);
        row.PublishSubPath = request.PublishSubPath.Trim();
        row.BackupSubPath = request.BackupSubPath.Trim();
        row.BackupRetentionCount = request.BackupRetentionCount;
        row.ComposeFilePath = request.ComposeFilePath.Trim();
        row.ComposeProjectName = string.IsNullOrWhiteSpace(request.ComposeProjectName) ? null : request.ComposeProjectName.Trim();
        row.ServiceName = string.IsNullOrWhiteSpace(request.ServiceName) ? null : request.ServiceName.Trim();
        row.ContainerName = string.IsNullOrWhiteSpace(request.ContainerName) ? null : request.ContainerName.Trim();
        row.ExternalNetworkName = string.IsNullOrWhiteSpace(request.ExternalNetworkName) ? null : request.ExternalNetworkName.Trim();
        row.UseDownWithVolumesOnDeploy = request.UseDownWithVolumesOnDeploy;
        row.HealthCheckType = request.HealthCheckType;
        row.HealthCheckEndpoint = string.IsNullOrWhiteSpace(request.HealthCheckEndpoint) ? null : request.HealthCheckEndpoint.Trim();
        row.HealthCheckIntervalSeconds = request.HealthCheckIntervalSeconds;
        row.HealthCheckTimeoutSeconds = request.HealthCheckTimeoutSeconds;
        row.ApplicationUrl = NormalizedUrlOrNull(request.ApplicationUrl);
        row.IsActive = request.IsActive;
        row.UpdatedAt = DateTimeOffset.UtcNow;

        if (isNew)
            db.ApplicationEnvironments.Add(row);
        await db.SaveChangesAsync(cancellationToken);

        await auditService.LogAsync(
            isNew ? "application.environment.create" : "application.environment.update",
            AuditResult.Success, "ApplicationEnvironment", row.Id.ToString(),
            details: $"{application.Name}/{environmentDefinition.Name} -> server '{targetServer.Name}', root '{row.DeploymentRootPath}'",
            cancellationToken: cancellationToken);

        row.EnvironmentDefinition = environmentDefinition;
        row.TargetServer = targetServer;
        return ToDto(row);
    }

    private static void ValidateForMode(DeploymentMode mode, UpsertApplicationEnvironmentRequest request, TargetServer targetServer)
    {
        if (!DeploymentPathValidator.IsSafeRelativePath(request.ComposeFilePath))
            throw new ValidationException("ComposeFilePath must be a relative path with no '.' or '..' segments.");
        if (!DeploymentPathValidator.IsSafeRelativePath(request.PublishSubPath))
            throw new ValidationException("PublishSubPath must be a relative path with no '.' or '..' segments.");
        if (!DeploymentPathValidator.IsSafeRelativePath(request.BackupSubPath))
            throw new ValidationException("BackupSubPath must be a relative path with no '.' or '..' segments.");

        if (mode == DeploymentMode.LegacyFilesystem)
        {
            if (string.IsNullOrWhiteSpace(request.DeploymentRootPath))
                throw new ValidationException("DeploymentRootPath is required for legacy filesystem deployment mode.");

            if (!DeploymentPathValidator.TryNormalize(request.DeploymentRootPath, out var normalizedRoot))
                throw new ValidationException("DeploymentRootPath must be an absolute path with no '.' or '..' segments.");

            var activeAllowedRoots = targetServer.AllowedDeploymentRoots.Where(r => r.IsActive).Select(r => r.RootPath);
            if (!DeploymentPathValidator.IsUnderAllowedRoot(normalizedRoot, activeAllowedRoots))
            {
                throw new ValidationException(
                    $"DeploymentRootPath '{normalizedRoot}' is not under any allowed deployment root configured for target server '{targetServer.Name}'.");
            }
        }

        if (request.HealthCheckType != HealthCheckType.None)
        {
            if (string.IsNullOrWhiteSpace(request.HealthCheckEndpoint))
                throw new ValidationException("HealthCheckEndpoint is required when HealthCheckType is not None.");
            if (request.HealthCheckIntervalSeconds <= 0 || request.HealthCheckTimeoutSeconds <= 0)
                throw new ValidationException("HealthCheckIntervalSeconds and HealthCheckTimeoutSeconds must be positive.");
        }

        if (!string.IsNullOrWhiteSpace(request.ApplicationUrl) && !IsSafeApplicationUrl(request.ApplicationUrl))
            throw new ValidationException("ApplicationUrl must be an absolute http(s) URL with no embedded credentials.");
    }

    /// <summary>Same validation Repository.Url uses (RepositoryService.ValidateUrl) — absolute
    /// http(s), no embedded userinfo credentials. Duplicated rather than shared because the two
    /// entities' URL fields have independent optionality/error-message requirements.</summary>
    private static bool IsSafeApplicationUrl(string url) =>
        Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) &&
        uri.Scheme is "https" or "http" &&
        string.IsNullOrEmpty(uri.UserInfo);

    private static string? NormalizedOrNull(string? path) =>
        DeploymentPathValidator.TryNormalize(path, out var normalized) ? normalized : null;

    private static string? NormalizedUrlOrNull(string? url) =>
        string.IsNullOrWhiteSpace(url) ? null : url.Trim();

    private async Task<ApplicationEnvironment?> LoadAsync(Guid applicationId, Guid environmentDefinitionId, CancellationToken cancellationToken) =>
        await db.ApplicationEnvironments
            .Include(ae => ae.EnvironmentDefinition)
            .Include(ae => ae.TargetServer)
            .FirstOrDefaultAsync(ae => ae.ApplicationId == applicationId && ae.EnvironmentDefinitionId == environmentDefinitionId, cancellationToken);

    private static ApplicationEnvironmentDto ToDto(ApplicationEnvironment ae) => new(
        ae.Id, ae.ApplicationId, ae.EnvironmentDefinitionId, ae.EnvironmentDefinition.Name,
        ae.TargetServerId, ae.TargetServer.Name, ae.BranchName, ae.DeploymentRootPath,
        ae.PublishSubPath, ae.BackupSubPath, ae.BackupRetentionCount, ae.ComposeFilePath,
        ae.ComposeProjectName, ae.ServiceName, ae.ContainerName, ae.ExternalNetworkName, ae.UseDownWithVolumesOnDeploy,
        ae.HealthCheckType, ae.HealthCheckEndpoint, ae.HealthCheckIntervalSeconds, ae.HealthCheckTimeoutSeconds,
        ae.ApplicationUrl, ae.IsActive, ae.CreatedAt, ae.UpdatedAt);

    public async Task<GitProviderResult<GitCommitInfo>> GetLatestCommitAsync(
        Guid applicationId, Guid environmentDefinitionId, CancellationToken cancellationToken = default)
    {
        var (repository, branch) = await ResolveRepositoryAndBranchAsync(applicationId, environmentDefinitionId, cancellationToken);
        return await gitProviderClient.GetLatestCommitAsync(repository, branch, cancellationToken);
    }

    public async Task<GitProviderResult<IReadOnlyList<GitCommitInfo>>> GetRecentCommitsAsync(
        Guid applicationId, Guid environmentDefinitionId, int count, CancellationToken cancellationToken = default)
    {
        var (repository, branch) = await ResolveRepositoryAndBranchAsync(applicationId, environmentDefinitionId, cancellationToken);
        return await gitProviderClient.GetRecentCommitsAsync(repository, branch, count, cancellationToken);
    }

    private async Task<(Repository Repository, string Branch)> ResolveRepositoryAndBranchAsync(
        Guid applicationId, Guid environmentDefinitionId, CancellationToken cancellationToken)
    {
        var application = await db.Applications.FirstOrDefaultAsync(a => a.Id == applicationId, cancellationToken)
            ?? throw new NotFoundException("Application", applicationId);
        if (application.RepositoryId is null)
            throw new ValidationException("This application has no repository configured.");

        var repository = await db.Repositories.FirstOrDefaultAsync(r => r.Id == application.RepositoryId, cancellationToken)
            ?? throw new ValidationException("The configured repository could not be found.");

        var appEnv = await LoadAsync(applicationId, environmentDefinitionId, cancellationToken)
            ?? throw new NotFoundException("ApplicationEnvironment", $"{applicationId}/{environmentDefinitionId}");
        if (string.IsNullOrWhiteSpace(appEnv.BranchName))
            throw new ValidationException("This application-environment has no branch configured.");

        return (repository, appEnv.BranchName);
    }
}
