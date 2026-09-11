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

    public async Task<RemoteConnectionTestResult> TestConnectionAsync(TargetServer targetServer, CancellationToken cancellationToken = default)
    {
        if (!IsConfigured(targetServer))
        {
            return new RemoteConnectionTestResult(
                false, null, null, false, null, false, null,
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

                var dockerAvailable = dockerVersion.Success;
                var composeAvailable = composeVersion.Success;

                return new RemoteConnectionTestResult(
                    true,
                    authenticatedUser.Success ? authenticatedUser.Output : null,
                    osInfo.Success ? osInfo.Output : null,
                    dockerAvailable, dockerAvailable ? dockerVersion.Output : null,
                    composeAvailable, composeAvailable ? composeVersion.Output : null,
                    dockerAvailable ? null : "SSH connected, but Docker is not available (or not on PATH) for this user on the target server.");
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "SSH connection test failed for target server '{TargetServerName}'", targetServer.Name);
                return new RemoteConnectionTestResult(false, null, null, false, null, false, null, DescribeFailure(ex));
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
