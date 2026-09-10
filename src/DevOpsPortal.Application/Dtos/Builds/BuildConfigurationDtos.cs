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
    Guid? BuildServerId,
    string? BuildServerName,
    string? JobName,
    string? SdkVersion,
    string? PublishArguments,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);

/// <summary>BuildServerId/JobName/SdkVersion/PublishArguments default to null so
/// every existing Legacy-only caller (no build-from-source configured) keeps
/// compiling and behaving exactly as before — see master requirements §7.</summary>
public record UpsertBuildConfigurationRequest(
    string? ProjectOrSolutionPath,
    string? PublishConfiguration,
    string? DockerfilePath,
    string? ImageRegistry,
    string? ImageRepository,
    ImageTagStrategy ImageTagStrategy,
    Guid? BuildServerId = null,
    string? JobName = null,
    string? SdkVersion = null,
    string? PublishArguments = null);
