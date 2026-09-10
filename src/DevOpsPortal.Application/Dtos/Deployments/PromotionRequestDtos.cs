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
    string? DecidedByUsername,
    DateTimeOffset? DecidedAt,
    string? DecisionNotes,
    DateTimeOffset? NotifiedAt,
    bool RequiresCtoApproval,
    ApprovalStatus? CtoApprovalStatus,
    Guid? CtoDecidedByUserId,
    string? CtoDecidedByUsername,
    DateTimeOffset? CtoDecidedAt,
    DateTimeOffset? CtoNotifiedAt,
    Guid? LinkedDeploymentId,
    DeploymentStatus? LinkedDeploymentStatus);

public record CreatePromotionRequest(Guid SourceDeploymentId);

public record DecidePromotionRequest(string? Notes);

/// <summary>Read-only, unauthenticated preview for an approval-requested
/// notification's deep link — deliberately carries no secret and nothing an
/// authenticated GET wouldn't also show; it exists only so a recipient can
/// see what they're being asked to review before logging in. Never a path to
/// approve/reject — see ApprovalTokenHelper's doc comment. IsExpired/
/// IsDecided let the caller render "this link has expired" / "already
/// decided" instead of pretending the token is still active.</summary>
public record ApprovalPreviewDto(
    Guid Id,
    string ApplicationName,
    string? FromEnvironmentName,
    string ToEnvironmentName,
    string CommitSha,
    string? RequestedByUsername,
    DateTimeOffset RequestedAt,
    ApprovalStatus Status,
    DateTimeOffset ExpiresAt,
    bool IsExpired);
