using DevOpsPortal.Application.Common;
using DevOpsPortal.Application.Dtos.Builds;
using DevOpsPortal.Application.Exceptions;
using DevOpsPortal.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace DevOpsPortal.Application.Services;

public class BuildConfigurationService(IAppDbContext db, IAuditService auditService) : IBuildConfigurationService
{
    public async Task<BuildConfigurationDto?> GetAsync(Guid applicationId, CancellationToken cancellationToken = default)
    {
        if (!await db.Applications.AnyAsync(a => a.Id == applicationId, cancellationToken))
            throw new NotFoundException("Application", applicationId);

        var config = await db.BuildConfigurations.FirstOrDefaultAsync(bc => bc.ApplicationId == applicationId, cancellationToken);
        return config is null ? null : ToDto(config);
    }

    public async Task<BuildConfigurationDto> UpsertAsync(
        Guid applicationId, UpsertBuildConfigurationRequest request, CancellationToken cancellationToken = default)
    {
        if (!await db.Applications.AnyAsync(a => a.Id == applicationId, cancellationToken))
            throw new NotFoundException("Application", applicationId);

        ValidateRelativePathIfSet(request.ProjectOrSolutionPath, nameof(request.ProjectOrSolutionPath));
        ValidateRelativePathIfSet(request.DockerfilePath, nameof(request.DockerfilePath));

        var config = await db.BuildConfigurations.FirstOrDefaultAsync(bc => bc.ApplicationId == applicationId, cancellationToken);
        var isNew = config is null;
        config ??= new BuildConfiguration { ApplicationId = applicationId };

        config.ProjectOrSolutionPath = NormalizeOrNull(request.ProjectOrSolutionPath);
        config.PublishConfiguration = NormalizeOrNull(request.PublishConfiguration);
        config.DockerfilePath = NormalizeOrNull(request.DockerfilePath);
        config.ImageRegistry = NormalizeOrNull(request.ImageRegistry);
        config.ImageRepository = NormalizeOrNull(request.ImageRepository);
        config.ImageTagStrategy = request.ImageTagStrategy;
        config.UpdatedAt = DateTimeOffset.UtcNow;

        if (isNew)
            db.BuildConfigurations.Add(config);
        await db.SaveChangesAsync(cancellationToken);

        await auditService.LogAsync(isNew ? "buildconfiguration.create" : "buildconfiguration.update", AuditResult.Success,
            "BuildConfiguration", config.Id.ToString(), details: $"application {applicationId}", cancellationToken: cancellationToken);

        return ToDto(config);
    }

    private static void ValidateRelativePathIfSet(string? path, string fieldName)
    {
        if (!string.IsNullOrWhiteSpace(path) && !DeploymentPathValidator.IsSafeRelativePath(path))
            throw new ValidationException($"{fieldName} must be a relative path with no '.' or '..' segments.");
    }

    private static string? NormalizeOrNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static BuildConfigurationDto ToDto(BuildConfiguration c) => new(
        c.Id, c.ApplicationId, c.ProjectOrSolutionPath, c.PublishConfiguration, c.DockerfilePath,
        c.ImageRegistry, c.ImageRepository, c.ImageTagStrategy, c.CreatedAt, c.UpdatedAt);
}
