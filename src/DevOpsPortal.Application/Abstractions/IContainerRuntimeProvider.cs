using DevOpsPortal.Domain.Entities;
using DevOpsPortal.Domain.Enums;

namespace DevOpsPortal.Application.Abstractions;

/// <summary>A single `docker stats --no-stream` snapshot for one container.
/// CPU/Memory percentages are parsed to numbers; memory usage/limit and
/// network/block I/O are kept as Docker's own human-readable formatted
/// strings (e.g. "12MiB", "1.2GiB / 3.4GB") rather than parsed into exact
/// byte counts — that's what `docker stats` itself reports, and re-deriving
/// exact bytes from unit-suffixed text adds real parsing fragility for a
/// monitoring display that doesn't need byte-exact precision. Any field can
/// be null if Docker's own output omitted or malformed it.</summary>
public record ContainerStatsInfo(
    double? CpuPercent,
    string? MemoryUsage,
    string? MemoryLimit,
    double? MemoryPercent,
    string? NetworkIO,
    string? BlockIO,
    int? PidCount);

/// <summary>One container's live status, as reported by the target server's
/// Docker engine. Never constructed from user input — ServiceName/
/// ContainerName/Image come back from `docker compose ps`/`docker inspect`
/// themselves, scoped to a single already-validated compose project
/// directory on a specific TargetServer. Stats is null when a
/// `docker stats` snapshot wasn't taken or failed — a status result is never
/// blocked on that (inspect and stats are two independent remote calls; one
/// failing must not hide the other's data).</summary>
public record ContainerStatusInfo(
    string ServiceName,
    string ContainerName,
    string Image,
    string? ImageTag,
    ContainerState State,
    string? DockerHealthStatus,
    DateTimeOffset? StartedAt,
    int RestartCount,
    IReadOnlyList<string> Ports,
    ContainerStatsInfo? Stats = null);

/// <summary>`IsReachable = false` means the target server's Docker engine could
/// not be reached at all, so `Success`/`Logs`/`Error` are meaningless.
/// `IsReachable = true, Success = false` covers every other failure —
/// unknown/not-yet-discovered container name, or the `docker logs` command
/// itself failing — `Error` explains which.</summary>
public record ContainerLogsResult(bool IsReachable, bool Success, string Logs, string? Error);

/// <summary>`IsReachable = false` means the target server's Docker engine could not
/// be reached at all (today: always, since no secure remote execution mechanism
/// exists yet — see IRemoteExecutionProvider) and `Containers` is meaningless;
/// `IsReachable = true` with an empty `Containers` list means the target server
/// *was* reached but nothing matched (a real, distinct outcome once remote
/// execution exists).</summary>
public record ContainerRuntimeStatusResult(bool IsReachable, string? UnreachableReason, IReadOnlyList<ContainerStatusInfo> Containers);

/// <summary>`IsReachable = false` means the operation was never attempted because
/// the target server's Docker engine could not be reached; `Success` is only
/// meaningful when `IsReachable` is true.</summary>
public record ContainerRuntimeOperationResult(bool IsReachable, bool Success, string Message);

/// <summary>
/// Docker/Compose-domain facade `ContainerOperationsService` depends on for
/// every container status/control need — it is the ONLY thing that service
/// knows about, and it knows nothing about *how* a target server is actually
/// reached (that's <see cref="IRemoteExecutionProvider"/>'s job, which this
/// interface's implementation is built on top of). Splitting the two matters
/// because this layer owns real, always-relevant logic (discovering
/// containers via `compose ps`, enriching with `docker inspect`, mapping
/// Docker's state/health vocabulary via ContainerStateMapper, honoring the
/// ServiceName filter) that must stay correct and tested regardless of
/// whether real remote connectivity exists yet.
/// </summary>
public interface IContainerRuntimeProvider
{
    Task<ContainerRuntimeStatusResult> GetStatusAsync(
        TargetServer targetServer, string workingDirectory, string composeFilePath, string? projectName,
        CancellationToken cancellationToken = default);

    Task<ContainerRuntimeOperationResult> RunOperationAsync(
        TargetServer targetServer, string workingDirectory, string composeFilePath, string? projectName, ComposeOperation operation,
        CancellationToken cancellationToken = default);

    /// <summary>Fetches recent logs for one container of this compose project.
    /// `containerName` is re-validated against this same project's own
    /// `compose ps` discovery before anything is fetched — a caller-supplied
    /// name is never trusted blindly, even though it already passed the
    /// remote-execution layer's own plausible-Docker-name check, so an
    /// authorized user for THIS application environment can never read logs
    /// for an unrelated container that happens to share the same target
    /// server.</summary>
    Task<ContainerLogsResult> GetLogsAsync(
        TargetServer targetServer, string workingDirectory, string composeFilePath, string? projectName, string containerName, int tailLines,
        CancellationToken cancellationToken = default);
}
