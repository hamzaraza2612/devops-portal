using DevOpsPortal.Domain.Enums;

namespace DevOpsPortal.Application.Abstractions;

/// <summary>One container's live status, as reported by the Docker engine. Never
/// constructed from user input — ServiceName/ContainerName/Image come back from
/// `docker compose ps`/`docker inspect` themselves, scoped to a single already-
/// validated compose project directory (see IContainerInspector).</summary>
public record ContainerStatusInfo(
    string ServiceName,
    string ContainerName,
    string Image,
    string? ImageTag,
    ContainerState State,
    string? DockerHealthStatus,
    DateTimeOffset? StartedAt,
    int RestartCount,
    IReadOnlyList<string> Ports);

/// <summary>
/// Read-only container inspection for a configured application environment.
/// Implementations must only ever target the working directory/compose file
/// already validated by Phase 2's path allow-listing (same inputs
/// IComposeCommandExecutor takes) — container names/ids used for the detailed
/// `docker inspect` step must be *discovered* from that directory's own
/// `docker compose ps` output, never accepted from a caller. Never throws for
/// expected failure modes (directory/compose file missing, Docker unreachable,
/// no matching container) — returns an empty list instead, so a status page
/// can never crash because a container happens to not exist yet.
/// </summary>
public interface IContainerInspector
{
    Task<IReadOnlyList<ContainerStatusInfo>> GetStatusAsync(
        string workingDirectory, string composeFilePath, string? projectName, CancellationToken cancellationToken = default);
}
