namespace DevOpsPortal.Application.Abstractions;

/// <summary>The only operations the deployment engine is allowed to run — this
/// enum IS the allow-list (master requirements §5/§18: "execute only
/// predefined/configured deployment operations... never allow arbitrary
/// commands"). There is no way to pass a free-form command through this API.</summary>
public enum ComposeOperation
{
    Up,
    Down,
    DownWithVolumes,

    /// <summary>`docker compose restart` — Phase 5 operational control, distinct from
    /// the deploy-time Down/Up cycle above.</summary>
    Restart,

    /// <summary>`docker compose start` — starts existing (e.g. Exited) containers
    /// without recreating them.</summary>
    Start,

    /// <summary>`docker compose stop` — stops running containers without removing them.</summary>
    Stop,

    /// <summary>`docker compose ps -a --format json` — read-only status query, the
    /// first step of container inspection (see IContainerInspector). Never mutates
    /// anything.</summary>
    Ps,
}

/// <summary>EnvironmentVariables (name -> plaintext value, e.g. resolved secrets)
/// are passed as real process environment variables, never as command-line
/// arguments or written to any file — so a compose file can reference them via
/// `${VAR}` interpolation without the value ever appearing in `ps` output, a
/// log, or an exception. Optional and additive: omitting it (the default)
/// behaves exactly as before this field existed.</summary>
public record ComposeCommandRequest(
    string WorkingDirectory, string ComposeFilePath, string? ProjectName, ComposeOperation Operation,
    IReadOnlyDictionary<string, string>? EnvironmentVariables = null);

public record ComposeCommandResult(bool Success, int ExitCode, string StandardOutput, string StandardError);

/// <summary>
/// Executes `docker compose` against the Docker daemon reachable from this
/// process (see PROJECT_STATE.md — remote TargetServer execution over
/// SSH/an agent is not implemented yet). Always invokes the `docker` CLI
/// directly with an argument array (never a shell string), so there is no
/// command-injection surface regardless of what's in configuration.
/// </summary>
public interface IComposeCommandExecutor
{
    Task<ComposeCommandResult> RunAsync(ComposeCommandRequest request, CancellationToken cancellationToken = default);
}
