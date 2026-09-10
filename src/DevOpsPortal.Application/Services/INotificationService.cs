using DevOpsPortal.Domain.Entities;

namespace DevOpsPortal.Application.Services;

/// <summary>
/// Owns "who gets told about event X, and what does it say" for every
/// notification-worthy event in the deployment/approval workflow (master
/// requirements §2). Recipients are always resolved by permission, not role
/// name (same principle Phase 3 established for the original CTO email) —
/// a tenant can rename or regrant a permission without any code change here.
/// Every method is best-effort: a notification-provider failure (or no
/// recipients currently holding the relevant permission) is logged and
/// swallowed, never thrown — notification delivery must never block or
/// unwind the workflow action that triggered it.
/// </summary>
public interface INotificationService
{
    /// <summary>QA/UAT (non-production) approval requested. Requires
    /// promotion.Application/.FromEnvironmentDefinition/.ToEnvironmentDefinition
    /// to be loaded. permissionCode is the already-resolved
    /// deployments.approve.{qa,uat} code for promotion's target environment.
    /// rawApprovalToken is the one-time token minted for this promotion — never
    /// persisted, used only to build the notification's deep link. Sets
    /// promotion.NotifiedAt/NotificationRecipients and saves.</summary>
    Task NotifyPromotionApprovalRequestedAsync(
        PromotionRequest promotion, string permissionCode, string rawApprovalToken, CancellationToken cancellationToken = default);

    /// <summary>Production/CTO approval requested — the one notification event
    /// that covers both "production approval requested" and "CTO approval
    /// requested" from master requirements §2: in this system,
    /// deployments.approve.production is the single permission that gates
    /// both, so they are the same recipients and the same email. Sets
    /// approval.NotifiedAt/NotificationRecipients and saves.</summary>
    Task NotifyProductionApprovalRequestedAsync(
        PromotionRequest promotion, ProductionApproval approval, string rawApprovalToken, CancellationToken cancellationToken = default);

    /// <summary>Requires deployment.Application/.EnvironmentDefinition loaded.
    /// Notifies the user who requested the deployment (deployment.RequestedByUserId).</summary>
    Task NotifyDeploymentStartedAsync(Deployment deployment, CancellationToken cancellationToken = default);

    /// <summary>Covers deployment succeeded/failed and rollback completed/failed
    /// — wording is chosen from deployment.IsRollback and deployment.Status
    /// (must already be Succeeded or Failed). Requires deployment.Application/
    /// .EnvironmentDefinition loaded.</summary>
    Task NotifyDeploymentOutcomeAsync(Deployment deployment, CancellationToken cancellationToken = default);
}
