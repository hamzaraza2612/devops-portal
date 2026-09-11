namespace DevOpsPortal.Domain.Entities;

/// <summary>
/// A host that runs application containers. Phase 2 models configuration only —
/// no remote connection (SSH/API) is established to it yet.
/// </summary>
public class TargetServer
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    /// <summary>Reserved for later phases (remote Docker/SSH access). Not contacted in Phase 2.</summary>
    public string? Hostname { get; set; }

    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public ICollection<AllowedDeploymentRoot> AllowedDeploymentRoots { get; set; } = new List<AllowedDeploymentRoot>();
}
