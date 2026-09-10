namespace DevOpsPortal.Domain.Entities;

/// <summary>
/// A whitelisted base directory on a <see cref="TargetServer"/>
/// (e.g. "/mnt/data/techbey-apps"). Every legacy-mode
/// ApplicationEnvironment.DeploymentRootPath on that server must resolve
/// under one of these — the server-side allow-list backing master
/// requirements §20 ("use allow-listed deployment targets").
/// </summary>
public class AllowedDeploymentRoot
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid TargetServerId { get; set; }
    public TargetServer TargetServer { get; set; } = null!;

    public string RootPath { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
