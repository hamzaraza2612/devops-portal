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
}

public record ComposeCommandRequest(string WorkingDirectory, string ComposeFilePath, string? ProjectName, ComposeOperation Operation);

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
