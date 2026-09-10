using DevOpsPortal.Domain.Enums;

namespace DevOpsPortal.Application.Dtos.Deployments;

public record PromotionRequestDto(
    Guid Id,
    Guid ApplicationId,
    string ApplicationName,
    Guid FromEnvironmentDefinitionId,
    string FromEnvironmentName,
    Guid ToEnvironmentDefinitionId,
    string ToEnvironmentName,
    Guid SourceDeploymentId,
    string CommitSha,
    ApprovalStatus Status,
    Guid RequestedByUserId,
    string? RequestedByUsername,
    DateTimeOffset RequestedAt,
    Guid? DecidedByUserId,
    DateTimeOffset? DecidedAt,
    string? DecisionNotes,
    bool RequiresCtoApproval,
    ApprovalStatus? CtoApprovalStatus,
    DateTimeOffset? CtoEmailSentAt);

public record CreatePromotionRequest(Guid SourceDeploymentId);

public record DecidePromotionRequest(string? Notes);
