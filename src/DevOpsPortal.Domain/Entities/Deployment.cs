using DevOpsPortal.Domain.Enums;

namespace DevOpsPortal.Domain.Entities;

/// <summary>
/// One deployment job: a specific commit (or image), deployed to a specific
/// application+environment, executed by the background worker. The
/// authoritative deployment history — every deploy and rollback creates one
/// of these, never mutated destructively.
/// </summary>
public class Deployment
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ApplicationId { get; set; }
    public ManagedApplication Application { get; set; } = null!;

    public Guid EnvironmentDefinitionId { get; set; }
    public EnvironmentDefinition EnvironmentDefinition { get; set; } = null!;

    /// <summary>Snapshot of which ApplicationEnvironment config row was used — config can
    /// change later without altering the historical record of what was actually deployed.</summary>
    public Guid ApplicationEnvironmentId { get; set; }
    public ApplicationEnvironment ApplicationEnvironment { get; set; } = null!;

    public string CommitSha { get; set; } = string.Empty;
    public string? CommitMessage { get; set; }
    public string? CommitAuthor { get; set; }
    public string? Branch { get; set; }

    /// <summary>Populated only for ContainerImage-mode deployments once a build/registry pipeline exists.</summary>
    public string? ImageReference { get; set; }

    /// <summary>Free-form build/version label (e.g. a CI build number), independent of CommitSha.</summary>
    public string? VersionLabel { get; set; }

    public DeploymentStatus Status { get; set; } = DeploymentStatus.Pending;

    public bool IsRollback { get; set; }
    public Guid? RollbackOfDeploymentId { get; set; }
    public Deployment? RollbackOfDeployment { get; set; }

    /// <summary>The approved PromotionRequest that authorized this deploy (QA/UAT/Production).
    /// Null for a direct DEV deploy, which needs no promotion/approval.</summary>
    public Guid? PromotionRequestId { get; set; }
    public PromotionRequest? PromotionRequest { get; set; }

    public Guid RequestedByUserId { get; set; }
    public DateTimeOffset RequestedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }

    public string? FailureReason { get; set; }

    public bool? HealthCheckPassed { get; set; }
    public string? HealthCheckDetail { get; set; }

    public ICollection<DeploymentLogEntry> LogEntries { get; set; } = new List<DeploymentLogEntry>();
}
