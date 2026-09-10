using DevOpsPortal.Domain.Enums;

namespace DevOpsPortal.Application.Dtos.Builds;

public record BuildConfigurationDto(
    Guid Id,
    Guid ApplicationId,
    string? ProjectOrSolutionPath,
    string? PublishConfiguration,
    string? DockerfilePath,
    string? ImageRegistry,
    string? ImageRepository,
    ImageTagStrategy ImageTagStrategy,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);

public record UpsertBuildConfigurationRequest(
    string? ProjectOrSolutionPath,
    string? PublishConfiguration,
    string? DockerfilePath,
    string? ImageRegistry,
    string? ImageRepository,
    ImageTagStrategy ImageTagStrategy);
