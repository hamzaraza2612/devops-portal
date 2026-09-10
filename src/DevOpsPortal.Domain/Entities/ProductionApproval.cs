using DevOpsPortal.Domain.Enums;

namespace DevOpsPortal.Domain.Entities;

/// <summary>
/// The CTO-specific approval gate for a Production PromotionRequest. Created
/// automatically when a Production promotion is requested. ApprovalToken is
/// an unguessable *reference* used only to deep-link the notification email
/// to the right pending approval in the portal — it is not a credential and
/// never bypasses authentication/authorization; approving still requires an
/// authenticated user holding the production-approval permission.
/// </summary>
public class ProductionApproval
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid PromotionRequestId { get; set; }
    public PromotionRequest PromotionRequest { get; set; } = null!;

    public string ApprovalToken { get; set; } = string.Empty;

    public ApprovalStatus Status { get; set; } = ApprovalStatus.PendingApproval;

    public DateTimeOffset RequestedAt { get; set; } = DateTimeOffset.UtcNow;

    public Guid? DecidedByUserId { get; set; }
    public DateTimeOffset? DecidedAt { get; set; }
    public string? DecisionNotes { get; set; }

    public DateTimeOffset? EmailSentAt { get; set; }

    /// <summary>Snapshot of who the notification was sent to (role membership can change later).</summary>
    public string? EmailRecipients { get; set; }
}
