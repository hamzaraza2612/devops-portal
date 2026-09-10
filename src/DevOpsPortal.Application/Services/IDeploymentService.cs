using DevOpsPortal.Application.Dtos.Deployments;
using DevOpsPortal.Domain.Enums;

namespace DevOpsPortal.Application.Services;

/// <summary>
/// The deployment/promotion state machine. Every mutating method here
/// re-validates everything server-side (existence, RBAC for the resolved
/// environment, current state, concurrency) — callers must never rely on the
/// frontend to have already checked any of this. Approval and deployment are
/// always separate calls; nothing here auto-deploys as a side effect of
/// another action succeeding.
/// </summary>
public interface IDeploymentService
{
    Task<IReadOnlyList<DeploymentDto>> ListAsync(
        Guid? applicationId, Guid? environmentDefinitionId, DeploymentStatus? status, CancellationToken cancellationToken = default);

    Task<DeploymentDto> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DeploymentLogEntryDto>> GetLogsAsync(Guid id, CancellationToken cancellationToken = default);

    Task<DeploymentStatusSummaryDto> GetStatusAsync(Guid applicationId, Guid environmentDefinitionId, CancellationToken cancellationToken = default);

    /// <summary>DEV has no promotion/approval gate — this is the only direct deploy entry point.</summary>
    Task<DeploymentDto> DeployToDevAsync(Guid applicationId, CreateDevDeploymentRequest request, CancellationToken cancellationToken = default);

    Task<PromotionRequestDto> RequestPromotionAsync(
        Guid applicationId, Guid toEnvironmentDefinitionId, CreatePromotionRequest request, CancellationToken cancellationToken = default);

    /// <summary>By default, only promotions still awaiting approval (Status == PendingApproval).
    /// With <paramref name="includeApprovedAwaitingDeploy"/>, also includes promotions that have
    /// been approved but have no Deployment row yet — i.e. still need someone to click "Deploy"
    /// (Phase 4 UI's "what still needs action" view; approving never creates that Deployment row
    /// itself, so without this an approved-but-undeployed promotion would otherwise be
    /// undiscoverable through this list endpoint once it leaves PendingApproval).</summary>
    Task<IReadOnlyList<PromotionRequestDto>> ListPendingPromotionsAsync(
        Guid? applicationId, Guid? toEnvironmentDefinitionId, bool includeApprovedAwaitingDeploy = false, CancellationToken cancellationToken = default);

    Task<PromotionRequestDto> GetPromotionAsync(Guid promotionRequestId, CancellationToken cancellationToken = default);

    /// <summary>Unauthenticated, read-only preview for a promotion's
    /// approval-requested notification deep link — never a path to approve or
    /// reject (master requirements §4). Throws NotFoundException if the token
    /// doesn't match any promotion.</summary>
    Task<ApprovalPreviewDto> GetPromotionPreviewByTokenAsync(string token, CancellationToken cancellationToken = default);

    /// <summary>Same contract as GetPromotionPreviewByTokenAsync, for the
    /// CTO-specific ProductionApproval record.</summary>
    Task<ApprovalPreviewDto> GetProductionApprovalPreviewByTokenAsync(string token, CancellationToken cancellationToken = default);

    /// <summary>For QA/UAT this directly approves. For Production this grants the linked
    /// CTO ProductionApproval — there is no separate non-CTO approval step for Production.</summary>
    Task<PromotionRequestDto> ApprovePromotionAsync(Guid promotionRequestId, DecidePromotionRequest request, CancellationToken cancellationToken = default);

    Task<PromotionRequestDto> RejectPromotionAsync(Guid promotionRequestId, DecidePromotionRequest request, CancellationToken cancellationToken = default);

    /// <summary>Deploys an Approved promotion (and, for Production, one whose CTO approval is
    /// also Approved) — a distinct explicit action from approval itself.</summary>
    Task<DeploymentDto> DeployApprovedPromotionAsync(Guid promotionRequestId, CancellationToken cancellationToken = default);

    Task<DeploymentDto> RollbackAsync(
        Guid applicationId, Guid environmentDefinitionId, RollbackRequest request, CancellationToken cancellationToken = default);
}
