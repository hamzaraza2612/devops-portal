using DevOpsPortal.Domain.Enums;

namespace DevOpsPortal.Application.Dtos.Applications;

public record ApplicationDto(
    Guid Id,
    string Name,
    string Slug,
    string? Description,
    DeploymentMode DeploymentMode,
    Guid? RepositoryId,
    string? RepositoryName,
    string? SourcePath,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);

public record CreateApplicationRequest(
    string Name,
    string Slug,
    string? Description,
    DeploymentMode DeploymentMode,
    Guid? RepositoryId,
    string? SourcePath);

public record UpdateApplicationRequest(
    string Name,
    string? Description,
    DeploymentMode DeploymentMode,
    Guid? RepositoryId,
    string? SourcePath,
    bool IsActive);
