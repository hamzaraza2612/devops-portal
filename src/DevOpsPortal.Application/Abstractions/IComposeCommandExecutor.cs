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

    /// <summary>`docker compose pull` — pulls the image(s) a compose file's services
    /// reference (master requirements §17: "target server pulls immutable image
    /// from registry") before `Up` recreates containers from it. Only used by
    /// ContainerImage-mode deployments; LegacyFilesystem never pulls an image.</summary>
    Pull,
}

/// <summary>EnvironmentVariables (name -> plaintext value, e.g. resolved secrets)
/// let a compose file reference them via `${VAR}` interpolation without the
/// value appearing in command-line arguments a process listing could observe.
/// Consumed by <see cref="IRemoteExecutionProvider"/> (the only place a
/// ComposeCommandRequest is actually executed — see PROJECT_STATE.md's Phase 12
/// remote-execution notes for how each implementation passes these through:
/// SshRemoteExecutionProvider quotes each value into a `NAME='value'` prefix on
/// the remote command line, since SSH.NET has no equivalent of
/// ProcessStartInfo.Environment for a single remote command). Optional and
/// additive: omitting it (the default) behaves exactly as before this field
/// existed.</summary>
public record ComposeCommandRequest(
    string WorkingDirectory, string ComposeFilePath, string? ProjectName, ComposeOperation Operation,
    IReadOnlyDictionary<string, string>? EnvironmentVariables = null);

public record ComposeCommandResult(bool Success, int ExitCode, string StandardOutput, string StandardError);
