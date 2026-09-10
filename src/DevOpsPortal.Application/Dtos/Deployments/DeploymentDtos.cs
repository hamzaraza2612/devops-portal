using DevOpsPortal.Domain.Enums;

namespace DevOpsPortal.Application.Dtos.Deployments;

public record DeploymentDto(
    Guid Id,
    Guid ApplicationId,
    string ApplicationName,
    Guid EnvironmentDefinitionId,
    string EnvironmentName,
    string CommitSha,
    string? CommitMessage,
    string? CommitAuthor,
    string? Branch,
    string? ImageReference,
    string? VersionLabel,
    DeploymentStatus Status,
    bool IsRollback,
    Guid? RollbackOfDeploymentId,
    Guid? PromotionRequestId,
    Guid RequestedByUserId,
    string? RequestedByUsername,
    DateTimeOffset RequestedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    string? FailureReason,
    bool? HealthCheckPassed,
    string? HealthCheckDetail);

public record DeploymentLogEntryDto(int Sequence, DateTimeOffset Timestamp, DeploymentLogLevel Level, string Message);

/// <summary>Body for POST /api/applications/{applicationId}/environments/dev/deployments.
/// CommitMessage/CommitAuthor are caller-supplied (typically copied from a prior commit-lookup
/// call) rather than re-fetched server-side, so deployment creation never depends on GitLab
/// being reachable at that exact moment.</summary>
public record CreateDevDeploymentRequest(string CommitSha, string? CommitMessage, string? CommitAuthor, string? Branch);

public record RollbackRequest(Guid TargetDeploymentId);

public record DeploymentStatusSummaryDto(
    Guid ApplicationId,
    Guid EnvironmentDefinitionId,
    string EnvironmentName,
    DeploymentDto? CurrentDeployment,
    DeploymentDto? LatestAttempt,
    Guid? PendingPromotionRequestId);
