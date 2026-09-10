namespace DevOpsPortal.Domain.Enums;

/// <summary>Shared by PromotionRequest (QA/UAT/Production approval gate) and
/// ProductionApproval (the CTO-specific approval record for Production promotions).</summary>
public enum ApprovalStatus
{
    PendingApproval = 0,
    Approved = 1,
    Rejected = 2,
}
