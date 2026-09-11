using DevOpsPortal.Domain.Entities;

namespace DevOpsPortal.Application.Abstractions;

/// <summary>Result of a `docker inspect &lt;name&gt;` call against a specific target
/// server. `RawJson` is the untouched `docker inspect` output for the caller to
/// parse — kept as a provider-agnostic string rather than a provider-specific
/// object so a future SSH/agent-based implementation only needs to produce the
/// same JSON text an equivalent local call would, without this interface
/// knowing anything about *how* it got there.</summary>
public record RemoteContainerInspectResult(bool Success, string RawJson, string? Error);

/// <summary>Result of a "Test Connection" check against a TargetServer (master
/// requirements §6): verifies SSH connectivity, the authenticated remote user,
/// basic OS identification, and Docker/Compose availability + versions. Never
/// fakes a successful result — when SshConnected is false every other field is
/// meaningless and ErrorMessage explains why.</summary>
public record RemoteConnectionTestResult(
    bool SshConnected,
    string? AuthenticatedUser,
    string? OsInfo,
    bool DockerAvailable,
    string? DockerVersion,
    bool ComposeAvailable,
    string? ComposeVersion,
    string? ErrorMessage);

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

    /// <summary>Master requirements §6 "Test Connection": actually connects and
    /// verifies SSH connectivity, the authenticated user, and Docker/Compose
    /// availability — never fabricates a successful result.</summary>
    Task<RemoteConnectionTestResult> TestConnectionAsync(TargetServer targetServer, CancellationToken cancellationToken = default);
}
