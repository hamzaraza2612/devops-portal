using System.Text.RegularExpressions;

namespace DevOpsPortal.Infrastructure.Remote;

/// <summary>
/// SSH.NET has no argv-array equivalent of Process.ArgumentList — every remote
/// command is a single string the SSH server hands to a shell (master
/// requirements §23: "command injection protection"). Every dynamic value
/// (paths, project names, env var values, container names) MUST go through
/// <see cref="Quote"/> before being concatenated into a command string; nothing
/// in this assembly should ever interpolate an unquoted value into a remote
/// command.
/// </summary>
internal static partial class PosixShellEscaper
{
    /// <summary>Single-quote escaping is safe for any byte sequence in POSIX
    /// sh/bash: wrap in single quotes, and turn each literal single quote into
    /// close-quote + escaped-quote + reopen-quote (`'\''`).</summary>
    public static string Quote(string value) => "'" + value.Replace("'", "'\\''") + "'";

    /// <summary>Environment variable names interpolated unquoted as `NAME=value`
    /// (the value itself is still quoted) — so the name itself must be a strict
    /// shell identifier, never containing `=`, whitespace, or shell metacharacters.</summary>
    public static bool IsSafeEnvVarName(string name) => EnvVarNamePattern().IsMatch(name);

    /// <summary>Defense-in-depth for container names passed to `docker inspect`:
    /// callers are only ever supposed to pass a name discovered from this same
    /// target server's own `docker compose ps` output, never user input, but this
    /// still rejects anything that isn't a plausible Docker name before it's
    /// quoted and sent.</summary>
    public static bool IsSafeDockerName(string name) => DockerNamePattern().IsMatch(name);

    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]*$")]
    private static partial Regex EnvVarNamePattern();

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9_.-]*$")]
    private static partial Regex DockerNamePattern();
}
