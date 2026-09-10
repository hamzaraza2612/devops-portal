using DevOpsPortal.Domain.Enums;

namespace DevOpsPortal.Application.Dtos.Applications;

public record ApplicationEnvironmentDto(
    Guid Id,
    Guid ApplicationId,
    Guid EnvironmentDefinitionId,
    string EnvironmentName,
    Guid TargetServerId,
    string TargetServerName,
    string? BranchName,
    string? DeploymentRootPath,
    string PublishSubPath,
    string BackupSubPath,
    int? BackupRetentionCount,
    string ComposeFilePath,
    string? ComposeProjectName,
    string? ServiceName,
    string? ContainerName,
    string? ExternalNetworkName,
    bool UseDownWithVolumesOnDeploy,
    HealthCheckType HealthCheckType,
    string? HealthCheckEndpoint,
    int HealthCheckIntervalSeconds,
    int HealthCheckTimeoutSeconds,
    string? ApplicationUrl,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);

/// <summary>Body for PUT /api/applications/{applicationId}/environments/{environmentDefinitionId}
/// — both ids come from the route; this upserts the single config row for that pair.</summary>
public record UpsertApplicationEnvironmentRequest(
    Guid TargetServerId,
    string? BranchName,
    string? DeploymentRootPath,
    string PublishSubPath,
    string BackupSubPath,
    int? BackupRetentionCount,
    string ComposeFilePath,
    string? ComposeProjectName,
    string? ServiceName,
    string? ContainerName,
    string? ExternalNetworkName,
    bool UseDownWithVolumesOnDeploy,
    HealthCheckType HealthCheckType,
    string? HealthCheckEndpoint,
    int HealthCheckIntervalSeconds,
    int HealthCheckTimeoutSeconds,
    string? ApplicationUrl,
    bool IsActive);
