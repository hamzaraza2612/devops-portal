namespace DevOpsPortal.Domain.Enums;

/// <summary>Deployment job lifecycle. Set only by the deployment engine (Application/Infrastructure),
/// never directly by an API caller — state transitions are validated server-side.</summary>
public enum DeploymentStatus
{
    Pending = 0,
    Queued = 1,
    Running = 2,
    Succeeded = 3,
    Failed = 4,
    Cancelled = 5,
}
