using DevOpsPortal.Application.Abstractions;
using DevOpsPortal.Domain.Enums;

namespace DevOpsPortal.Application.Dtos.Infrastructure;

/// <summary>Structured host metrics (see DockerDiscoveryParser) — every field
/// is independently nullable and never fabricated: a null field means that
/// one metric's command failed or its output didn't parse, not zero.</summary>
public record HostMetricsDto(
    double? Load1,
    double? Load5,
    double? Load15,
    long? MemTotalBytes,
    long? MemUsedBytes,
    long? MemAvailableBytes,
    long? DiskTotalBytes,
    long? DiskUsedBytes,
    long? DiskAvailableBytes,
    double? DiskUsePercent);

/// <summary>The "SERVER" section of the Environment Infrastructure Dashboard.
/// `IsConfigured = false` means this environment has no PrimaryTargetServer
/// assigned yet (a distinct, honest state from "assigned but unreachable" —
/// see EnvironmentDefinition.PrimaryTargetServerId). When `IsConfigured` is
/// true but `SshConnected` is false, every other field except `ErrorMessage`
/// is meaningless — never fabricated.</summary>
public record EnvironmentServerInfoDto(
    Guid? TargetServerId,
    string? TargetServerName,
    string? Hostname,
    bool IsConfigured,
    bool SshConnected,
    string? AuthenticatedUser,
    string? OsInfo,
    string? UptimeInfo,
    bool DockerAvailable,
    string? DockerVersion,
    bool ComposeAvailable,
    string? ComposeVersion,
    HostMetricsDto? Metrics,
    string? ErrorMessage,
    DateTimeOffset RetrievedAt);

/// <summary>One container discovered directly on the target server (`docker ps
/// -a`), independent of whether any Application/ApplicationEnvironment row
/// references it. `IsMapped = false` means exactly that: a real, currently
/// running-or-stopped container that has no configured application behind it
/// — the dashboard must show it anyway, never hide it (master requirement:
/// "do not hide a real running container just because it has no Application
/// row in the database").</summary>
public record DiscoveredContainerDto(
    string ContainerId,
    string Name,
    string Image,
    string? ImageTag,
    ContainerState State,
    string? DockerHealthStatus,
    DateTimeOffset? CreatedAt,
    DateTimeOffset? StartedAt,
    int RestartCount,
    IReadOnlyList<string> Ports,
    ContainerStatsInfo? Stats,
    bool IsMapped,
    Guid? ApplicationId,
    string? ApplicationName,
    Guid? EnvironmentDefinitionId,
    /// <summary>True only for a mapped container — recreate-with-volumes needs a
    /// known compose file/working directory, which only a configured
    /// ApplicationEnvironment provides (see RemoteContainerAction's doc comment
    /// for why this isn't offered for unmapped containers).</summary>
    bool CanRecreateWithVolumes);

/// <summary>The full Environment Infrastructure Dashboard payload for one
/// pipeline stage — real server status plus every real container discovered
/// on it, refreshed on every request (never cached database state).</summary>
public record EnvironmentInfrastructureDto(
    Guid EnvironmentDefinitionId,
    string EnvironmentName,
    EnvironmentServerInfoDto Server,
    IReadOnlyList<DiscoveredContainerDto> Containers);

public record ContainerActionRequest(string ContainerId);

public record DiscoveredContainerLogsRequest(string ContainerId, int TailLines);

public record DiscoveredContainerLogsDto(string ContainerId, bool Success, string Logs, string? Error);
