using DevOpsPortal.Domain.Enums;

namespace DevOpsPortal.Domain.Entities;

/// <summary>
/// A request to promote one application's exact successful build (identified
/// by SourceDeployment, never re-derived) from one environment to the next.
/// Approval and deployment are deliberately separate: reaching Approved here
/// only makes a deploy *possible* — it never triggers one. For Production,
/// approval additionally requires the linked <see cref="ProductionApproval"/>
/// (CTO) to be Approved.
/// </summary>
public class PromotionRequest
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }

    public Guid ApplicationId { get; set; }
    public ManagedApplication Application { get; set; } = null!;

    public Guid FromEnvironmentDefinitionId { get; set; }
    public EnvironmentDefinition FromEnvironmentDefinition { get; set; } = null!;

    public Guid ToEnvironmentDefinitionId { get; set; }
    public EnvironmentDefinition ToEnvironmentDefinition { get; set; } = null!;

    /// <summary>The exact successful deployment being promoted — "build once, promote the same artifact".</summary>
    public Guid SourceDeploymentId { get; set; }
    public Deployment SourceDeployment { get; set; } = null!;

    /// <summary>Denormalized from SourceDeployment for quick display/audit; always kept in sync at creation.</summary>
    public string CommitSha { get; set; } = string.Empty;

    public ApprovalStatus Status { get; set; } = ApprovalStatus.PendingApproval;

    public Guid RequestedByUserId { get; set; }
    public DateTimeOffset RequestedAt { get; set; } = DateTimeOffset.UtcNow;

    public Guid? DecidedByUserId { get; set; }
    public DateTimeOffset? DecidedAt { get; set; }
    public string? DecisionNotes { get; set; }

    /// <summary>Hash (SHA-256, hex) of a one-time random token minted when this
    /// request is created — never the raw token, which exists only transiently
    /// (in memory, for inclusion in the approval-requested notification) and is
    /// never persisted or logged. Used only to resolve
    /// GET /api/promotions/by-token/{token} to a read-only preview of this
    /// request for an unauthenticated notification-email recipient; deciding
    /// (approve/reject/deploy) always requires the normal authenticated,
    /// permission-checked endpoints — the token is never itself a bypass
    /// credential (same design principle as ProductionApproval.ApprovalTokenHash).</summary>
    public string ApprovalTokenHash { get; set; } = string.Empty;

    /// <summary>After this, GET .../by-token/{token} reports the link as expired
    /// rather than resolving it — bounds how long a leaked/forwarded email link
    /// remains useful even though it only ever grants read access.</summary>
    public DateTimeOffset ApprovalTokenExpiresAt { get; set; }

    /// <summary>Null if no active user held the relevant approve.* permission at
    /// request time, or the notification provider failed to send — the request
    /// itself is never blocked either way (master requirements: notification
    /// delivery must never gate the workflow, same principle as Phase 3's CTO
    /// email).</summary>
    public DateTimeOffset? NotifiedAt { get; set; }

    /// <summary>Snapshot of who the "approval requested" notification was sent
    /// to (role/permission membership can change later) — comma-joined,
    /// audit/display only, never a secret.</summary>
    public string? NotificationRecipients { get; set; }

    /// <summary>Only populated when ToEnvironmentDefinition is Production.</summary>
    public ProductionApproval? ProductionApproval { get; set; }
}
