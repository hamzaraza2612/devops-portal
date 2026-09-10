using DevOpsPortal.Domain.Entities;
using DevOpsPortal.Domain.Enums;

namespace DevOpsPortal.Application.Abstractions;

/// <summary>One container's live status, as reported by the target server's
/// Docker engine. Never constructed from user input — ServiceName/
/// ContainerName/Image come back from `docker compose ps`/`docker inspect`
/// themselves, scoped to a single already-validated compose project
/// directory on a specific TargetServer.</summary>
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
}
