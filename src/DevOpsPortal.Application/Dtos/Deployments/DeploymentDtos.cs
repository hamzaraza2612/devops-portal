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
/// For a LegacyFilesystem-mode application: CommitSha is required (CommitMessage/CommitAuthor
/// are caller-supplied, typically copied from a prior commit-lookup call, rather than
/// re-fetched server-side, so deployment creation never depends on GitLab being reachable at
/// that exact moment) and ReleaseId must be omitted. For a ContainerImage-mode application
/// (master requirements §16/§17): ReleaseId is required and must reference one of this
/// application's own Releases — CommitSha/CommitMessage/CommitAuthor/Branch/ImageReference are
/// all derived from that immutable Release server-side, never taken from the request body, so
/// what gets deployed always matches a real, already-built image.</summary>
public record CreateDevDeploymentRequest(string? CommitSha, string? CommitMessage, string? CommitAuthor, string? Branch, Guid? ReleaseId = null);

public record RollbackRequest(Guid TargetDeploymentId);

public record DeploymentStatusSummaryDto(
    Guid ApplicationId,
    Guid EnvironmentDefinitionId,
    string EnvironmentName,
    DeploymentDto? CurrentDeployment,
    DeploymentDto? LatestAttempt,
    Guid? PendingPromotionRequestId);
