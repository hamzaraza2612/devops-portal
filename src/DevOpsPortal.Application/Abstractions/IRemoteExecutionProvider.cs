using DevOpsPortal.Domain.Entities;

namespace DevOpsPortal.Application.Abstractions;

/// <summary>Result of a `docker inspect &lt;name&gt;` call against a specific target
/// server. `RawJson` is the untouched `docker inspect` output for the caller to
/// parse — kept as a provider-agnostic string rather than a provider-specific
/// object so a future SSH/agent-based implementation only needs to produce the
/// same JSON text an equivalent local call would, without this interface
/// knowing anything about *how* it got there.</summary>
public record RemoteContainerInspectResult(bool Success, string RawJson, string? Error);

/// <summary>Result of a `docker logs --tail N &lt;name&gt;` call — `Logs` is the raw
/// (container stdout+stderr, merged) text. Never fabricates output: `Success`
/// false means the command itself failed (unreachable, no such container,
/// Docker error) and `Logs` is empty; `Error` explains why.</summary>
public record RemoteContainerLogsResult(bool Success, string Logs, string? Error);

/// <summary>Result of a `docker stats --no-stream --format '{{json .}}' &lt;name&gt;`
/// call. `RawJson` is the untouched single-object JSON line for the caller to
/// parse (same provider-agnostic-string pattern as
/// <see cref="RemoteContainerInspectResult.RawJson"/>).</summary>
public record RemoteContainerStatsResult(bool Success, string RawJson, string? Error);

/// <summary>Result of a "Test Connection" check against a TargetServer (master
/// requirements §6): verifies SSH connectivity, the authenticated remote user,
/// basic OS identification, Docker/Compose availability + versions, and
/// host-level resource info (uptime/load, memory, disk — Phase 13 "Environment
/// Server Dashboard"). Each host-metric field is the raw output of a single
/// read-only command (`uptime`, `free -h`, `df -h`) rather than parsed
/// numbers — same provider-agnostic-string pattern as OsInfo, and it means a
/// parsing failure can never fabricate a number. Never fakes a successful
/// result — when SshConnected is false every other field is meaningless and
/// ErrorMessage explains why; when SshConnected is true but an individual
/// host-metric command fails, that one field is null rather than failing the
/// whole test.</summary>
public record RemoteConnectionTestResult(
    bool SshConnected,
    string? AuthenticatedUser,
    string? OsInfo,
    bool DockerAvailable,
    string? DockerVersion,
    bool ComposeAvailable,
    string? ComposeVersion,
    string? UptimeInfo,
    string? MemoryInfo,
    string? DiskInfo,
    string? ErrorMessage);

/// <summary>Result of whole-server container discovery — `docker ps -aq` piped
/// into `docker inspect` (every container on the host, running or stopped,
/// regardless of which compose project or application it belongs to) plus a
/// separate `docker stats --no-stream --format '{{json .}}'` with no name
/// filter (every currently-running container's live resource usage in one
/// shot). Both commands are entirely fixed strings — no caller-supplied value
/// is ever interpolated into either, so there is no injection surface here at
/// all: discovery lists what exists, it never targets one caller-named thing.
/// `InspectJson` is `"[]"` (a valid empty JSON array) when the host has zero
/// containers — a real, distinct outcome from `Success = false`, which means
/// the commands themselves could not be run (SSH/Docker unavailable).
/// `StatsJson` is one JSON object per line (docker's own format for stats
/// without `--no-stream` name filtering), empty when nothing is running.</summary>
public record RemoteContainerDiscoveryResult(bool Success, string InspectJson, string StatsJson, string? Error);

/// <summary>The fixed, safe set of direct (non-compose) container actions
/// available for a container discovered outside any configured
/// ApplicationEnvironment — a container with no known compose file, so
/// `docker compose &lt;op&gt;` (which needs one) doesn't apply. Deliberately does
/// NOT include "recreate with volumes": that is a compose-level operation
/// (`down -v` + `up -d`) that requires knowing the compose project, and stays
/// available only through the existing ComposeOperation path for containers
/// that ARE mapped to a configured application.</summary>
public enum RemoteContainerAction
{
    Start,
    Stop,
    Restart,
}

/// <summary>Result of a direct `docker start|stop|restart &lt;id&gt;` call.</summary>
public record RemoteContainerActionResult(bool Success, string Message, string? Error);

/// <summary>Machine-parseable host metrics for the Environment Infrastructure
/// Dashboard's summary cards — deliberately a *separate* method from
/// <see cref="RemoteConnectionTestResult"/>'s UptimeInfo/MemoryInfo/DiskInfo
/// rather than a change to them: those stay the existing human-readable
/// `uptime`/`free -h`/`df -h` text used by the admin "Test Connection" panel,
/// unchanged. This uses `/proc/loadavg`, `free -b` (exact bytes), and
/// `df -Pk` (exact 1024-byte blocks, POSIX-stable column layout) specifically
/// because they're reliably machine-parseable without guessing at unit
/// suffixes — see DockerDiscoveryParser for the parsing itself. Each raw field
/// is null independently on that one command's failure, same honesty
/// convention as everywhere else in this interface.</summary>
public record RemoteHostMetricsResult(bool Success, string? LoadAvgRaw, string? MemRaw, string? DiskRaw, string? Error);

/// <summary>One connected SSH session's worth of everything the Environment
/// Infrastructure Dashboard needs — connection/Docker/Compose status, host
/// metrics, and (only when Docker is reachable) whole-server container
/// discovery — combined into a single round trip so one dashboard load/
/// refresh costs one SSH connect+auth handshake, not three separate ones.
///
/// <para>This exists because composing the equivalent from
/// <see cref="IRemoteExecutionProvider.TestConnectionAsync"/> +
/// <see cref="IRemoteExecutionProvider.GetHostMetricsAsync"/> run
/// concurrently (e.g. via <c>Task.WhenAll</c>) is actively unsafe, not just
/// slower: each independently resolves the stored SSH credential through the
/// same scoped <c>ISecretProvider</c>, which for the real
/// <c>EncryptedSecretProvider</c> means two concurrent queries against the
/// same scoped <c>DbContext</c> — EF Core throws "A second operation was
/// started on this context instance before a previous operation completed."
/// Sequential-but-separate calls would avoid the crash but still pay for
/// three SSH handshakes; this method is the one place that does the whole
/// job in a single connected session.</para></summary>
public record RemoteEnvironmentSnapshotResult(
    bool SshConnected,
    string? AuthenticatedUser,
    string? OsInfo,
    bool DockerAvailable,
    string? DockerVersion,
    bool ComposeAvailable,
    string? ComposeVersion,
    string? UptimeInfo,
    string? LoadAvgRaw,
    string? MemRaw,
    string? DiskRaw,
    string InspectJson,
    string StatsJson,
    string? ErrorMessage);

/// <summary>Result of syncing a downloaded source archive onto a target
/// server's filesystem (the "obtain/update source" step of a LegacyFilesystem
/// deployment — see ApplicationEnvironment.SyncSourceFromRepository). Never
/// fabricates success: a failure at upload, extraction, or cleanup is
/// reported as-is, and <c>ExtractedEntryCount</c> is null unless the target
/// server actually reported one (via `tar`'s own verbose listing), so a
/// caller can never mistake "the command exited 0" for "files were actually
/// written" without evidence.</summary>
public record RemoteSourceSyncResult(bool Success, int? ExtractedEntryCount, string? Error);

/// <summary>
/// The portal's ONLY boundary for reaching a specific TargetServer's Docker
/// engine. This interface exists because master requirements for Phase 5
/// (corrected) make explicit what Phase 3's known-limitations notes already
/// flagged: <b>the portal runs on its own VM, separate from every
/// TargetServer</b> — there is no local Docker socket that represents "the"
/// deployment target, and code must never assume there is one.
///
/// <para>Exactly one implementation exists today —
/// <c>NotConfiguredRemoteExecutionProvider</c> (Infrastructure) — and it is
/// honest about that: every target server reports as unreachable, and no
/// method ever spawns a local process pretending to be a stand-in for a
/// remote one. A future phase that adds a real secure remote-execution
/// mechanism (SSH with a credential vault, or a lightweight per-target-server
/// agent) implements this same interface and nothing above it (
/// <c>IContainerRuntimeProvider</c>, <c>ContainerOperationsService</c>, the
/// API surface) needs to change.</para>
///
/// <para>Never accepts a free-form command — only the same fixed
/// <see cref="ComposeOperation"/> enum Phase 3's local executor already
/// enforces, plus a single-container inspect keyed by a container *name*
/// that callers must only ever have discovered from this same target
/// server's own `docker compose ps` output (see
/// <c>IContainerRuntimeProvider</c>), never from user input.</para>
/// </summary>
public interface IRemoteExecutionProvider
{
    /// <summary>Whether this provider currently has a way to reach this specific
    /// target server's Docker engine at all — checked before attempting any real
    /// operation, so callers can report "not connected" without a doomed round
    /// trip. Always false until a real implementation is configured.</summary>
    bool IsConfigured(TargetServer targetServer);

    Task<ComposeCommandResult> RunComposeAsync(
        TargetServer targetServer, ComposeCommandRequest request, CancellationToken cancellationToken = default);

    Task<RemoteContainerInspectResult> InspectContainerAsync(
        TargetServer targetServer, string containerName, CancellationToken cancellationToken = default);

    /// <summary>`docker logs --tail N &lt;name&gt;` — same "callers must only ever pass
    /// a name discovered from this target server's own `docker compose ps`
    /// output" contract as <see cref="InspectContainerAsync"/> (enforced by the
    /// caller, <c>IContainerRuntimeProvider</c>, not here); this method itself
    /// still independently validates the name is a plausible Docker name as
    /// defense-in-depth before it is quoted and sent.</summary>
    Task<RemoteContainerLogsResult> GetContainerLogsAsync(
        TargetServer targetServer, string containerName, int tailLines, CancellationToken cancellationToken = default);

    /// <summary>`docker stats --no-stream --format '{{json .}}' &lt;name&gt;` — a
    /// single point-in-time snapshot (never the streaming/live form) for one
    /// already-discovered container. Same name-safety contract as
    /// <see cref="GetContainerLogsAsync"/>.</summary>
    Task<RemoteContainerStatsResult> GetContainerStatsAsync(
        TargetServer targetServer, string containerName, CancellationToken cancellationToken = default);

    /// <summary>Master requirements §6 "Test Connection": actually connects and
    /// verifies SSH connectivity, the authenticated user, and Docker/Compose
    /// availability — never fabricates a successful result.</summary>
    Task<RemoteConnectionTestResult> TestConnectionAsync(TargetServer targetServer, CancellationToken cancellationToken = default);

    /// <summary>Whole-server container discovery, independent of any configured
    /// ApplicationEnvironment/compose project — the basis of the Environment
    /// Infrastructure Dashboard's "show me every real container on this
    /// server" requirement. See <see cref="RemoteContainerDiscoveryResult"/>.</summary>
    Task<RemoteContainerDiscoveryResult> DiscoverContainersAsync(TargetServer targetServer, CancellationToken cancellationToken = default);

    /// <summary>`docker start|stop|restart &lt;id&gt;` — the direct-container
    /// counterpart to <see cref="RunComposeAsync"/> for containers with no known
    /// compose file (discovered but not mapped to a configured application).
    /// Same defense-in-depth contract as every other name-taking method here:
    /// the caller (the Application-layer service) must only ever pass an
    /// id/name it just re-confirmed exists via <see cref="DiscoverContainersAsync"/>
    /// on this same target server, and this method independently validates it
    /// looks like a plausible Docker identifier before quoting and sending it.</summary>
    Task<RemoteContainerActionResult> RunContainerActionAsync(
        TargetServer targetServer, string containerId, RemoteContainerAction action, CancellationToken cancellationToken = default);

    /// <summary>See <see cref="RemoteHostMetricsResult"/>.</summary>
    Task<RemoteHostMetricsResult> GetHostMetricsAsync(TargetServer targetServer, CancellationToken cancellationToken = default);

    /// <summary>See <see cref="RemoteEnvironmentSnapshotResult"/> — the
    /// Environment Infrastructure Dashboard's single-round-trip equivalent of
    /// TestConnectionAsync + GetHostMetricsAsync + DiscoverContainersAsync.</summary>
    Task<RemoteEnvironmentSnapshotResult> GetEnvironmentSnapshotAsync(TargetServer targetServer, CancellationToken cancellationToken = default);

    /// <summary>Uploads <paramref name="archiveBytes"/> (a gzipped tarball, as
    /// downloaded by <see cref="IGitProviderClient.DownloadRepositoryArchiveAsync"/>)
    /// to the target server and extracts it into <paramref name="destinationPath"/>
    /// — the "obtain/update source" step of a deployment that opts into
    /// ApplicationEnvironment.SyncSourceFromRepository. <paramref name="destinationPath"/>
    /// must already have been validated by the caller against the target server's
    /// AllowedDeploymentRoots (same contract as <see cref="RunComposeAsync"/>'s
    /// working directory) — this method does not re-derive that check, but it
    /// does create the directory if missing. <paramref name="excludePatterns"/>
    /// are `tar --exclude` glob patterns (e.g. "appsettings*.json") for files
    /// that must never be overwritten by a source sync (environment-specific
    /// config the target server itself owns) — every pattern is individually
    /// quoted, never concatenated into a shell-interpreted string. GitLab's
    /// archive has a single top-level `&lt;project&gt;-&lt;sha&gt;/` directory,
    /// which is stripped (`--strip-components=1`) so files land directly under
    /// destinationPath.</summary>
    Task<RemoteSourceSyncResult> SyncSourceArchiveAsync(
        TargetServer targetServer, string destinationPath, byte[] archiveBytes,
        IReadOnlyList<string> excludePatterns, CancellationToken cancellationToken = default);
}
