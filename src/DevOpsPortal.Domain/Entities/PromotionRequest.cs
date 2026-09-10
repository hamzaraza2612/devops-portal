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

    /// <summary>Only populated when ToEnvironmentDefinition is Production.</summary>
    public ProductionApproval? ProductionApproval { get; set; }
}
