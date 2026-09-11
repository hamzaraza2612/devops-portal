using DevOpsPortal.Domain.Enums;

namespace DevOpsPortal.Domain.Entities;

/// <summary>
/// The CTO-specific approval gate for a Production PromotionRequest. Created
/// automatically when a Production promotion is requested. ApprovalTokenHash
/// (SHA-256 of an unguessable, one-time raw token) is used only to deep-link
/// the notification email to a read-only preview of this specific approval —
/// it is not a credential and never bypasses authentication/authorization;
/// approving still requires an authenticated user holding
/// deployments.approve.production, via the normal
/// POST /api/promotions/{id}/approve endpoint. The raw token exists only
/// transiently (for inclusion in the notification body) and is never
/// persisted or logged — only its hash is stored, so a database read alone
/// can never reproduce a working link.
/// </summary>
public class ProductionApproval
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }

    public Guid PromotionRequestId { get; set; }
    public PromotionRequest PromotionRequest { get; set; } = null!;

    public string ApprovalTokenHash { get; set; } = string.Empty;

    /// <summary>After this, GET .../production-approvals/by-token/{token} reports
    /// the link as expired rather than resolving it.</summary>
    public DateTimeOffset ExpiresAt { get; set; }

    public ApprovalStatus Status { get; set; } = ApprovalStatus.PendingApproval;

    public DateTimeOffset RequestedAt { get; set; } = DateTimeOffset.UtcNow;

    public Guid? DecidedByUserId { get; set; }
    public DateTimeOffset? DecidedAt { get; set; }
    public string? DecisionNotes { get; set; }

    public DateTimeOffset? NotifiedAt { get; set; }

    /// <summary>Snapshot of who the notification was sent to (role membership can change later).</summary>
    public string? NotificationRecipients { get; set; }
}
