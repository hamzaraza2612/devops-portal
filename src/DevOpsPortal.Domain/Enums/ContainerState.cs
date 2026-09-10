namespace DevOpsPortal.Domain.Enums;

/// <summary>Normalized container lifecycle state shown in the portal, mapped from
/// the Docker Engine's own state string (plus its separate health-check status,
/// which takes priority when present and unhealthy — see ContainerStateMapper).
/// `Unknown` covers every case the portal can't confidently classify: the
/// container/stack isn't found, Docker itself is unreachable, or the engine
/// reports a state this enum doesn't enumerate (e.g. "dead", "removing").</summary>
public enum ContainerState
{
    Unknown = 0,
    Running = 1,
    Exited = 2,
    Restarting = 3,
    Paused = 4,
    Created = 5,
    Unhealthy = 6,
}
