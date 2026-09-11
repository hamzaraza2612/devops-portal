using DevOpsPortal.Domain.Enums;

namespace DevOpsPortal.Domain.Entities;

/// <summary>
/// An immutable release/version record — the deployable output of exactly one
/// successful <see cref="BuildRequest"/> (see master requirements §6:
/// "Deployment should reference an immutable release"). Created once, when its
/// BuildRequest resolves to BuildStatus.Succeeded, and never updated afterward;
/// a re-build produces a new BuildRequest and a new Release, never a mutation
/// of this one. Only the Modern (Jenkins → Docker image) path in this phase
/// produces Release rows — Legacy (prebuilt publish directory) deployments
/// have no Release and keep working exactly as Phase 3 left them (see
/// PROJECT_STATE.md's Phase 6 "Legacy support" section).
/// </summary>
public class Release
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }

    public Guid ApplicationId { get; set; }
    public ManagedApplication Application { get; set; } = null!;

    public Guid BuildRequestId { get; set; }
    public BuildRequest BuildRequest { get; set; } = null!;

    public string CommitSha { get; set; } = string.Empty;
    public string? Branch { get; set; }

    /// <summary>The provider build number this release was produced by — always set,
    /// since a Release is only ever created from a resolved, successful build.</summary>
    public int BuildNumber { get; set; }

    /// <summary>Full, traceable image reference, e.g.
    /// "registry.example.com/group/app:abc1234def0" — see BuildConfiguration.ImageTagStrategy
    /// for how the tag portion is chosen. Never "latest".</summary>
    public string ImageReference { get; set; } = string.Empty;

    /// <summary>Always Succeeded for a row that exists — kept as an explicit column
    /// (rather than implied by existence) because master requirements §6 lists
    /// "build status" as one of the record's required fields.</summary>
    public BuildStatus BuildStatus { get; set; } = BuildStatus.Succeeded;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
