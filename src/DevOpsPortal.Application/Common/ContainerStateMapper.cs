using DevOpsPortal.Domain.Enums;

namespace DevOpsPortal.Application.Common;

/// <summary>
/// Pure mapping from Docker's own state/health vocabulary to the portal's
/// ContainerState — no I/O, deliberately testable without a Docker daemon.
/// </summary>
public static class ContainerStateMapper
{
    /// <param name="dockerState">`docker inspect`'s `.State.Status` (e.g.
    /// "running", "exited", "restarting", "paused", "created", "dead").</param>
    /// <param name="dockerHealthStatus">`docker inspect`'s `.State.Health.Status`
    /// (e.g. "healthy", "unhealthy", "starting"), or null if the container
    /// defines no Docker-native HEALTHCHECK.</param>
    public static ContainerState Map(string? dockerState, string? dockerHealthStatus)
    {
        // A container's own Docker healthcheck reporting "unhealthy" is the most
        // actionable signal available and takes priority over "running" — an app
        // that's up but failing its healthcheck is exactly what an operator needs
        // to see first, not a green "Running" badge.
        if (string.Equals(dockerHealthStatus, "unhealthy", StringComparison.OrdinalIgnoreCase))
            return ContainerState.Unhealthy;

        return dockerState?.Trim().ToLowerInvariant() switch
        {
            "running" => ContainerState.Running,
            "exited" => ContainerState.Exited,
            "restarting" => ContainerState.Restarting,
            "paused" => ContainerState.Paused,
            "created" => ContainerState.Created,
            // "dead", "removing", anything unrecognized, or missing entirely.
            _ => ContainerState.Unknown,
        };
    }
}
