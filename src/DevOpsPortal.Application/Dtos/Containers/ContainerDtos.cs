using DevOpsPortal.Domain.Enums;

namespace DevOpsPortal.Application.Dtos.Containers;

/// <summary>A `docker stats --no-stream` snapshot for one container — see
/// Application.Abstractions.ContainerStatsInfo (this is its API-facing
/// equivalent) for why memory usage/limit and network/block I/O are kept as
/// Docker's own formatted strings rather than exact byte counts.</summary>
public record ContainerStatsDto(
    double? CpuPercent,
    string? MemoryUsage,
    string? MemoryLimit,
    double? MemoryPercent,
    string? NetworkIO,
    string? BlockIO,
    int? PidCount);

public record ContainerInfoDto(
    string ServiceName,
    string ContainerName,
    string Image,
    string? ImageTag,
    ContainerState State,
    string? DockerHealthStatus,
    DateTimeOffset? StartedAt,
    TimeSpan? Uptime,
    int RestartCount,
    IReadOnlyList<string> Ports,
    ContainerStatsDto? Stats);

/// <summary>Recent `docker logs --tail N` output for one container, fetched
/// on demand (never polled automatically) — see
/// ContainerOperationsService.GetLogsAsync.</summary>
public record ContainerLogsDto(string ContainerName, bool Success, string Logs);

/// <summary>Combines a live probe (run fresh on every status request, using the
/// same IHealthCheckProbe Phase 3's deployment engine uses) with the last
/// *persisted* successful result — which comes from Deployment.HealthCheckPassed/
/// CompletedAt, not a new history table, since deployment execution already
/// records exactly this.</summary>
public record HealthCheckStatusDto(
    HealthCheckType Type,
    bool? LastProbePassed,
    string? LastProbeDetail,
    DateTimeOffset LastProbeAt,
    DateTimeOffset? LastSuccessfulCheckAt);

/// <summary>`IsConfigured` = does this application environment have a compose
/// config row to operate on at all. `IsReachable` = independently, could the
/// target server's Docker engine actually be reached right now — today this is
/// always false (see NotConfiguredRemoteExecutionProvider / PROJECT_STATE.md),
/// so `Containers` is always empty and `UnreachableReason` always explains why,
/// regardless of `IsConfigured`. The two are deliberately separate: a caller
/// needs to know whether to say "configure this environment" versus "remote
/// monitoring isn't available yet" — those are different problems.</summary>
public record ContainerEnvironmentStatusDto(
    Guid ApplicationId,
    Guid EnvironmentDefinitionId,
    string EnvironmentName,
    bool IsConfigured,
    bool IsReachable,
    string? UnreachableReason,
    string? TargetServerName,
    string? ExpectedServiceName,
    string? ExpectedContainerName,
    IReadOnlyList<ContainerInfoDto> Containers,
    HealthCheckStatusDto? HealthCheck,
    string? CurrentImageOrVersion,
    DateTimeOffset? LastRestartAt,
    Guid? LatestDeploymentId,
    DeploymentStatus? LatestDeploymentStatus);

public record ContainerActionResultDto(bool Success, string Message, DateTimeOffset PerformedAt);

/// <summary>Requires an explicit `Confirm: true` — a server-side safety net
/// against a stray/replayed request, independent of whatever confirmation UX
/// a future frontend builds. See ContainerOperationsService.RecreateWithVolumesAsync.</summary>
public record RecreateWithVolumesRequest(bool Confirm);
