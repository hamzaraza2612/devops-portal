using DevOpsPortal.Application.Abstractions;
using DevOpsPortal.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace DevOpsPortal.Infrastructure.Remote;

/// <summary>
/// The only <see cref="IRemoteExecutionProvider"/> registered today. No secure
/// remote execution mechanism exists yet for this deployment topology (the
/// portal runs on its own VM, separate from every TargetServer — see
/// PROJECT_STATE.md's Phase 5 remote-execution correction) — no SSH credential
/// vault, no per-target-server agent. Rather than falling back to running
/// `docker`/`docker compose` as a *local* process (which would silently
/// inspect/control whatever the portal container itself can see — almost
/// certainly nothing, and never the actual configured TargetServer — while
/// looking like it worked), this provider is honest: every target server is
/// reported unreachable, and no method here ever spawns a process. Swap the DI
/// registration for a real implementation once secure remote connectivity is
/// built (SSH-based or agent-based); nothing above this interface needs to
/// change.
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

    private static string UnconfiguredMessage(TargetServer targetServer) =>
        $"No remote execution mechanism is configured for target server '{targetServer.Name}'. " +
        "Remote Docker/Compose access is not yet implemented for this portal deployment — see PROJECT_STATE.md.";
}
