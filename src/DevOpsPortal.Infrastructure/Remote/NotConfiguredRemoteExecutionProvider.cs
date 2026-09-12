using DevOpsPortal.Application.Abstractions;
using DevOpsPortal.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace DevOpsPortal.Infrastructure.Remote;

/// <summary>
/// Kept as a defensively-honest reference implementation of
/// <see cref="IRemoteExecutionProvider"/>: unlike falling back to running
/// `docker`/`docker compose` as a *local* process (which would silently
/// inspect/control whatever the portal container itself can see — almost
/// certainly nothing, and never the actual configured TargetServer — while
/// looking like it worked), every method here reports the target server as
/// unreachable and never spawns a process. Phase 12 replaced this as the
/// registered <see cref="IRemoteExecutionProvider"/> with
/// <c>SshRemoteExecutionProvider</c>, which is real once a TargetServer has
/// SSH credentials configured — this class stays available (and its shape is
/// still what any future non-SSH provider, e.g. an agent-based one, should
/// match) for a TargetServer that has no SSH connection details configured at
/// all, and for tests.
/// </summary>
public class NotConfiguredRemoteExecutionProvider(ILogger<NotConfiguredRemoteExecutionProvider> logger) : IRemoteExecutionProvider
{
    public bool IsConfigured(TargetServer targetServer) => false;

    public Task<ComposeCommandResult> RunComposeAsync(
        TargetServer targetServer, ComposeCommandRequest request, CancellationToken cancellationToken = default)
    {
        logger.LogInformation(
            "Remote container operation requested for target server '{TargetServerName}' but no remote execution mechanism is configured.",
            targetServer.Name);
        return Task.FromResult(new ComposeCommandResult(false, -1, string.Empty, UnconfiguredMessage(targetServer)));
    }

    public Task<RemoteContainerInspectResult> InspectContainerAsync(
        TargetServer targetServer, string containerName, CancellationToken cancellationToken = default) =>
        Task.FromResult(new RemoteContainerInspectResult(false, string.Empty, UnconfiguredMessage(targetServer)));

    public Task<RemoteContainerLogsResult> GetContainerLogsAsync(
        TargetServer targetServer, string containerName, int tailLines, CancellationToken cancellationToken = default) =>
        Task.FromResult(new RemoteContainerLogsResult(false, string.Empty, UnconfiguredMessage(targetServer)));

    public Task<RemoteContainerStatsResult> GetContainerStatsAsync(
        TargetServer targetServer, string containerName, CancellationToken cancellationToken = default) =>
        Task.FromResult(new RemoteContainerStatsResult(false, string.Empty, UnconfiguredMessage(targetServer)));

    public Task<RemoteConnectionTestResult> TestConnectionAsync(TargetServer targetServer, CancellationToken cancellationToken = default) =>
        Task.FromResult(new RemoteConnectionTestResult(false, null, null, false, null, false, null, null, null, null, UnconfiguredMessage(targetServer)));

    public Task<RemoteContainerDiscoveryResult> DiscoverContainersAsync(TargetServer targetServer, CancellationToken cancellationToken = default) =>
        Task.FromResult(new RemoteContainerDiscoveryResult(false, string.Empty, string.Empty, UnconfiguredMessage(targetServer)));

    public Task<RemoteContainerActionResult> RunContainerActionAsync(
        TargetServer targetServer, string containerId, RemoteContainerAction action, CancellationToken cancellationToken = default) =>
        Task.FromResult(new RemoteContainerActionResult(false, string.Empty, UnconfiguredMessage(targetServer)));

    public Task<RemoteHostMetricsResult> GetHostMetricsAsync(TargetServer targetServer, CancellationToken cancellationToken = default) =>
        Task.FromResult(new RemoteHostMetricsResult(false, null, null, null, UnconfiguredMessage(targetServer)));

    public Task<RemoteEnvironmentSnapshotResult> GetEnvironmentSnapshotAsync(TargetServer targetServer, CancellationToken cancellationToken = default) =>
        Task.FromResult(new RemoteEnvironmentSnapshotResult(
            false, null, null, false, null, false, null, null, null, null, null, string.Empty, string.Empty, UnconfiguredMessage(targetServer)));

    public Task<RemoteSourceSyncResult> SyncSourceArchiveAsync(
        TargetServer targetServer, string destinationPath, byte[] archiveBytes,
        IReadOnlyList<string> excludePatterns, CancellationToken cancellationToken = default) =>
        Task.FromResult(new RemoteSourceSyncResult(false, null, UnconfiguredMessage(targetServer)));

    private static string UnconfiguredMessage(TargetServer targetServer) =>
        $"No remote execution mechanism is configured for target server '{targetServer.Name}'. " +
        "Remote Docker/Compose access is not yet implemented for this portal deployment — see PROJECT_STATE.md.";
}
