using System.Text;
using DevOpsPortal.Application.Abstractions;
using DevOpsPortal.Domain.Entities;
using DevOpsPortal.Domain.Enums;
using DevOpsPortal.Infrastructure.Deployments;
using Microsoft.Extensions.Logging;
using Renci.SshNet;
using Renci.SshNet.Common;

namespace DevOpsPortal.Infrastructure.Remote;

/// <summary>
/// The real <see cref="IRemoteExecutionProvider"/> for this portal's target
/// architecture (master requirements §5/§7 — the portal VM is separate from
/// every TargetServer, so this is the only way containers actually get
/// inspected/controlled/deployed to). Connects over SSH using the credential
/// referenced by TargetServer.SshCredentialStoreKey (resolved through the same
/// ISecretProvider every other secret in the portal uses — see
/// SecretReferenceService), and only ever runs `docker compose`/`docker
/// inspect` with fully quoted arguments (see PosixShellEscaper) built from the
/// fixed ComposeOperation allow-list — never a free-form command from a
/// caller. IsConfigured(TargetServer) is false (never attempted) until a
/// TargetServer has a Hostname, SshUsername, and a stored credential.
/// </summary>
public class SshRemoteExecutionProvider(ISecretProvider secretProvider, ILogger<SshRemoteExecutionProvider> logger)
    : IRemoteExecutionProvider
{
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(120);

    /// <summary>Hard cap on `docker logs --tail`, independent of whatever the
    /// caller asked for — prevents a large/misconfigured tailLines value from
    /// pulling an excessive amount of text over the SSH connection.</summary>
    private const int MaxLogTailLines = 5000;

    public bool IsConfigured(TargetServer targetServer) =>
        !string.IsNullOrWhiteSpace(targetServer.Hostname) &&
        !string.IsNullOrWhiteSpace(targetServer.SshUsername) &&
        !string.IsNullOrWhiteSpace(targetServer.SshCredentialStoreKey);

    public async Task<ComposeCommandResult> RunComposeAsync(
        TargetServer targetServer, ComposeCommandRequest request, CancellationToken cancellationToken = default)
    {
        if (!IsConfigured(targetServer))
            return new ComposeCommandResult(false, -1, string.Empty, NotConfiguredMessage(targetServer));

        string commandText;
        try
        {
            commandText = BuildComposeCommand(request);
        }
        catch (InvalidOperationException ex)
        {
            return new ComposeCommandResult(false, -1, string.Empty, ex.Message);
        }

        var result = await RunRemoteCommandAsync(targetServer, commandText, cancellationToken);
        return new ComposeCommandResult(result.Success, result.ExitCode, result.StandardOutput, result.StandardError);
    }

    public async Task<RemoteContainerInspectResult> InspectContainerAsync(
        TargetServer targetServer, string containerName, CancellationToken cancellationToken = default)
    {
        if (!IsConfigured(targetServer))
            return new RemoteContainerInspectResult(false, string.Empty, NotConfiguredMessage(targetServer));

        // Defense-in-depth (master requirements §23): callers are only ever supposed
        // to pass a name discovered from this same target server's own `docker
        // compose ps` output, never user input — but this rejects anything that
        // doesn't look like a plausible Docker name regardless.
        if (!PosixShellEscaper.IsSafeDockerName(containerName))
            return new RemoteContainerInspectResult(false, string.Empty, "Invalid container name.");

        var commandText = $"docker inspect {PosixShellEscaper.Quote(containerName)}";
        var result = await RunRemoteCommandAsync(targetServer, commandText, cancellationToken);
        return new RemoteContainerInspectResult(result.Success, result.StandardOutput, result.Success ? null : result.StandardError);
    }

    public async Task<RemoteContainerLogsResult> GetContainerLogsAsync(
        TargetServer targetServer, string containerName, int tailLines, CancellationToken cancellationToken = default)
    {
        if (!IsConfigured(targetServer))
            return new RemoteContainerLogsResult(false, string.Empty, NotConfiguredMessage(targetServer));

        // Defense-in-depth (master requirements §23), same contract as
        // InspectContainerAsync — see IRemoteExecutionProvider.GetContainerLogsAsync.
        if (!PosixShellEscaper.IsSafeDockerName(containerName))
            return new RemoteContainerLogsResult(false, string.Empty, "Invalid container name.");

        var clampedTail = Math.Clamp(tailLines, 1, MaxLogTailLines);
        // 2>&1 merges the container's stderr into stdout — `docker logs` output is
        // meant to be read as one interleaved stream, and this also means a
        // command-level failure's error text is still captured in StandardOutput
        // below rather than lost on a separate channel.
        var commandText = $"docker logs --tail {clampedTail} {PosixShellEscaper.Quote(containerName)} 2>&1";
        var result = await RunRemoteCommandAsync(targetServer, commandText, cancellationToken);

        return result.Success
            ? new RemoteContainerLogsResult(true, result.StandardOutput, null)
            : new RemoteContainerLogsResult(false, string.Empty, string.IsNullOrWhiteSpace(result.StandardOutput) ? result.StandardError : result.StandardOutput);
    }

    public async Task<RemoteContainerStatsResult> GetContainerStatsAsync(
        TargetServer targetServer, string containerName, CancellationToken cancellationToken = default)
    {
        if (!IsConfigured(targetServer))
            return new RemoteContainerStatsResult(false, string.Empty, NotConfiguredMessage(targetServer));

        if (!PosixShellEscaper.IsSafeDockerName(containerName))
            return new RemoteContainerStatsResult(false, string.Empty, "Invalid container name.");

        var commandText = $"docker stats --no-stream --format {PosixShellEscaper.Quote("{{json .}}")} {PosixShellEscaper.Quote(containerName)}";
        var result = await RunRemoteCommandAsync(targetServer, commandText, cancellationToken);

        return result.Success
            ? new RemoteContainerStatsResult(true, result.StandardOutput, null)
            : new RemoteContainerStatsResult(false, string.Empty, string.IsNullOrWhiteSpace(result.StandardError) ? result.StandardOutput : result.StandardError);
    }

    public async Task<RemoteConnectionTestResult> TestConnectionAsync(TargetServer targetServer, CancellationToken cancellationToken = default)
    {
        if (!IsConfigured(targetServer))
        {
            return new RemoteConnectionTestResult(
                false, null, null, false, null, false, null, null, null, null,
                "SSH is not configured for this target server — Hostname, SshUsername, and a stored credential are all required.");
        }

        return await Task.Run(async () =>
        {
            SshClient? client = null;
            try
            {
                var connectionInfo = await BuildConnectionInfoAsync(targetServer, cancellationToken);
                client = new SshClient(connectionInfo);
                client.Connect();

                var authenticatedUser = RunQuick(client, "whoami");
                var osInfo = RunQuick(client, "uname -a");
                var dockerVersion = RunQuick(client, "docker version --format '{{.Server.Version}}'");
                var composeVersion = RunQuick(client, "docker compose version --short");
                var uptime = RunQuick(client, "uptime");
                var memory = RunQuick(client, "free -h");
                var disk = RunQuick(client, "df -h / 2>/dev/null || df -h .");

                var dockerAvailable = dockerVersion.Success;
                var composeAvailable = composeVersion.Success;

                return new RemoteConnectionTestResult(
                    true,
                    authenticatedUser.Success ? authenticatedUser.Output : null,
                    osInfo.Success ? osInfo.Output : null,
                    dockerAvailable, dockerAvailable ? dockerVersion.Output : null,
                    composeAvailable, composeAvailable ? composeVersion.Output : null,
                    uptime.Success ? uptime.Output : null,
                    memory.Success ? memory.Output : null,
                    disk.Success ? disk.Output : null,
                    dockerAvailable ? null : "SSH connected, but Docker is not available (or not on PATH) for this user on the target server.");
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "SSH connection test failed for target server '{TargetServerName}'", targetServer.Name);
                return new RemoteConnectionTestResult(false, null, null, false, null, false, null, null, null, null, DescribeFailure(ex));
            }
            finally
            {
                if (client is { IsConnected: true })
                    client.Disconnect();
                client?.Dispose();
            }
        }, cancellationToken);
    }

    public async Task<RemoteContainerDiscoveryResult> DiscoverContainersAsync(TargetServer targetServer, CancellationToken cancellationToken = default)
    {
        if (!IsConfigured(targetServer))
            return new RemoteContainerDiscoveryResult(false, string.Empty, string.Empty, NotConfiguredMessage(targetServer));

        return await Task.Run(async () =>
        {
            SshClient? client = null;
            try
            {
                var connectionInfo = await BuildConnectionInfoAsync(targetServer, cancellationToken);
                client = new SshClient(connectionInfo);
                client.Connect();

                // Entirely fixed strings — docker ps's own output (container IDs it
                // generated itself) is what feeds docker inspect, never anything the
                // caller supplied, so there is nothing here to quote or validate.
                var inspect = RunQuick(client, "ids=$(docker ps -aq); if [ -n \"$ids\" ]; then docker inspect $ids; else echo '[]'; fi");
                if (!inspect.Success)
                    return new RemoteContainerDiscoveryResult(false, string.Empty, string.Empty, "Failed to list containers on this target server (is Docker installed and reachable for this SSH user?).");

                var stats = RunQuick(client, "docker stats --no-stream --format '{{json .}}'");

                return new RemoteContainerDiscoveryResult(true, inspect.Output, stats.Success ? stats.Output : string.Empty, null);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Container discovery failed for target server '{TargetServerName}'", targetServer.Name);
                return new RemoteContainerDiscoveryResult(false, string.Empty, string.Empty, DescribeFailure(ex));
            }
            finally
            {
                if (client is { IsConnected: true })
                    client.Disconnect();
                client?.Dispose();
            }
        }, cancellationToken);
    }

    public async Task<RemoteContainerActionResult> RunContainerActionAsync(
        TargetServer targetServer, string containerId, RemoteContainerAction action, CancellationToken cancellationToken = default)
    {
        if (!IsConfigured(targetServer))
            return new RemoteContainerActionResult(false, string.Empty, NotConfiguredMessage(targetServer));

        if (!PosixShellEscaper.IsSafeDockerName(containerId))
            return new RemoteContainerActionResult(false, string.Empty, "Invalid container identifier.");

        var (verb, pastTense) = action switch
        {
            RemoteContainerAction.Start => ("start", "started"),
            RemoteContainerAction.Stop => ("stop", "stopped"),
            RemoteContainerAction.Restart => ("restart", "restarted"),
            _ => throw new ArgumentOutOfRangeException(nameof(action)),
        };

        var commandText = $"docker {verb} {PosixShellEscaper.Quote(containerId)}";
        var result = await RunRemoteCommandAsync(targetServer, commandText, cancellationToken);

        return result.Success
            ? new RemoteContainerActionResult(true, $"Container {pastTense}.", null)
            : new RemoteContainerActionResult(false, string.Empty, string.IsNullOrWhiteSpace(result.StandardError) ? result.StandardOutput : result.StandardError);
    }

    public async Task<RemoteHostMetricsResult> GetHostMetricsAsync(TargetServer targetServer, CancellationToken cancellationToken = default)
    {
        if (!IsConfigured(targetServer))
            return new RemoteHostMetricsResult(false, null, null, null, NotConfiguredMessage(targetServer));

        return await Task.Run(async () =>
        {
            SshClient? client = null;
            try
            {
                var connectionInfo = await BuildConnectionInfoAsync(targetServer, cancellationToken);
                client = new SshClient(connectionInfo);
                client.Connect();

                var loadAvg = RunQuick(client, "cat /proc/loadavg");
                var mem = RunQuick(client, "free -b");
                var disk = RunQuick(client, "df -Pk / 2>/dev/null || df -Pk .");

                return new RemoteHostMetricsResult(
                    true,
                    loadAvg.Success ? loadAvg.Output : null,
                    mem.Success ? mem.Output : null,
                    disk.Success ? disk.Output : null,
                    null);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Host metrics collection failed for target server '{TargetServerName}'", targetServer.Name);
                return new RemoteHostMetricsResult(false, null, null, null, DescribeFailure(ex));
            }
            finally
            {
                if (client is { IsConnected: true })
                    client.Disconnect();
                client?.Dispose();
            }
        }, cancellationToken);
    }

    private static (bool Success, string Output) RunQuick(SshClient client, string commandText)
    {
        using var command = client.CreateCommand(commandText);
        command.CommandTimeout = CommandTimeout;
        var output = command.Execute();
        return command.ExitStatus == 0 ? (true, output.Trim()) : (false, string.Empty);
    }

    private Task<(bool Success, int ExitCode, string StandardOutput, string StandardError)> RunRemoteCommandAsync(
        TargetServer targetServer, string commandText, CancellationToken cancellationToken) =>
        Task.Run(async () =>
        {
            SshClient? client = null;
            try
            {
                var connectionInfo = await BuildConnectionInfoAsync(targetServer, cancellationToken);
                client = new SshClient(connectionInfo);
                client.Connect();

                using var command = client.CreateCommand(commandText);
                command.CommandTimeout = CommandTimeout;
                var output = command.Execute();
                var exitCode = command.ExitStatus ?? -1;
                return (exitCode == 0, exitCode, output, command.Error);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "SSH command failed for target server '{TargetServerName}'", targetServer.Name);
                return (false, -1, string.Empty, DescribeFailure(ex));
            }
            finally
            {
                if (client is { IsConnected: true })
                    client.Disconnect();
                client?.Dispose();
            }
        }, cancellationToken);

    private async Task<ConnectionInfo> BuildConnectionInfoAsync(TargetServer targetServer, CancellationToken cancellationToken)
    {
        var host = targetServer.Hostname!;
        var port = targetServer.SshPort > 0 ? targetServer.SshPort : 22;
        var username = targetServer.SshUsername!;

        AuthenticationMethod auth;
        if (targetServer.SshAuthMethod == SshAuthMethod.PrivateKey)
        {
            var keyResult = await secretProvider.RetrieveAsync(targetServer.SshCredentialStoreKey!, cancellationToken);
            if (!keyResult.Success || string.IsNullOrEmpty(keyResult.Value))
                throw new InvalidOperationException("Failed to retrieve the stored SSH private key for this target server.");

            string? passphrase = null;
            if (!string.IsNullOrWhiteSpace(targetServer.SshPassphraseStoreKey))
            {
                var passphraseResult = await secretProvider.RetrieveAsync(targetServer.SshPassphraseStoreKey, cancellationToken);
                if (passphraseResult.Success)
                    passphrase = passphraseResult.Value;
            }

            using var keyStream = new MemoryStream(Encoding.UTF8.GetBytes(keyResult.Value));
            var keyFile = string.IsNullOrEmpty(passphrase) ? new PrivateKeyFile(keyStream) : new PrivateKeyFile(keyStream, passphrase);
            auth = new PrivateKeyAuthenticationMethod(username, keyFile);
        }
        else
        {
            var passwordResult = await secretProvider.RetrieveAsync(targetServer.SshCredentialStoreKey!, cancellationToken);
            if (!passwordResult.Success || passwordResult.Value is null)
                throw new InvalidOperationException("Failed to retrieve the stored SSH password for this target server.");
            auth = new PasswordAuthenticationMethod(username, passwordResult.Value);
        }

        return new ConnectionInfo(host, port, username, auth) { Timeout = ConnectTimeout };
    }

    /// <summary>`cd '<dir>' && VAR='value' ... docker compose -f '<file>' [-p '<project>'] <op-args>` —
    /// every dynamic segment individually quoted (master requirements §23:
    /// command injection protection). Environment variables (resolved secrets)
    /// are passed as a `NAME=value` prefix on the command line rather than
    /// written to any file — SSH.NET has no equivalent of
    /// ProcessStartInfo.Environment for a single remote command — so the value
    /// itself is still quoted and never appears anywhere else (not in `ps`
    /// output, not logged).</summary>
    private static string BuildComposeCommand(ComposeCommandRequest request)
    {
        var sb = new StringBuilder();
        sb.Append("cd ").Append(PosixShellEscaper.Quote(request.WorkingDirectory)).Append(" && ");

        if (request.EnvironmentVariables is not null)
        {
            foreach (var (key, value) in request.EnvironmentVariables)
            {
                if (!PosixShellEscaper.IsSafeEnvVarName(key))
                    throw new InvalidOperationException($"Refusing to run compose with unsafe environment variable name '{key}'.");
                sb.Append(key).Append('=').Append(PosixShellEscaper.Quote(value)).Append(' ');
            }
        }

        sb.Append("docker compose -f ").Append(PosixShellEscaper.Quote(request.ComposeFilePath));
        if (!string.IsNullOrWhiteSpace(request.ProjectName))
            sb.Append(" -p ").Append(PosixShellEscaper.Quote(request.ProjectName));

        foreach (var arg in ComposeOperationArgs.For(request.Operation))
            sb.Append(' ').Append(PosixShellEscaper.Quote(arg));

        return sb.ToString();
    }

    private static string NotConfiguredMessage(TargetServer targetServer) =>
        $"SSH is not configured for target server '{targetServer.Name}' (Hostname, SshUsername, and a stored credential are all required).";

    /// <summary>Never surfaces exception internals beyond SSH.NET's own message
    /// (master requirements §23 "safe error messages") — SshAuthenticationException/
    /// SshConnectionException/SocketException messages are already generic
    /// connectivity descriptions, never secret material.</summary>
    private static string DescribeFailure(Exception ex) => ex switch
    {
        SshAuthenticationException => $"SSH authentication failed: {ex.Message}",
        SshConnectionException => $"SSH connection failed: {ex.Message}",
        System.Net.Sockets.SocketException => $"Could not reach host: {ex.Message}",
        OperationCanceledException => "The operation timed out.",
        _ => $"SSH operation failed: {ex.Message}",
    };
}
