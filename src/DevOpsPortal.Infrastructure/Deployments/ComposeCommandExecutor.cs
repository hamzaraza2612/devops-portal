using System.Diagnostics;
using System.Text;
using DevOpsPortal.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace DevOpsPortal.Infrastructure.Deployments;

/// <summary>See IComposeCommandExecutor. Never touches a shell — runs
/// `docker` with an explicit argument array built only from the fixed
/// ComposeOperation enum plus the WorkingDirectory/ComposeFilePath/
/// ProjectName already validated by the caller (path-allow-listed
/// ApplicationEnvironment config, never raw user input).</summary>
public class ComposeCommandExecutor(ILogger<ComposeCommandExecutor> logger) : IComposeCommandExecutor
{
    public async Task<ComposeCommandResult> RunAsync(ComposeCommandRequest request, CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(request.WorkingDirectory))
        {
            return new ComposeCommandResult(false, -1, string.Empty,
                $"Deployment directory '{request.WorkingDirectory}' does not exist or is not accessible to this process.");
        }

        var composeFileFullPath = Path.Combine(request.WorkingDirectory, request.ComposeFilePath);
        if (!File.Exists(composeFileFullPath))
        {
            return new ComposeCommandResult(false, -1, string.Empty,
                $"Compose file '{request.ComposeFilePath}' was not found under '{request.WorkingDirectory}'.");
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = "docker",
            WorkingDirectory = request.WorkingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        startInfo.ArgumentList.Add("compose");
        startInfo.ArgumentList.Add("-f");
        startInfo.ArgumentList.Add(request.ComposeFilePath);
        if (!string.IsNullOrWhiteSpace(request.ProjectName))
        {
            startInfo.ArgumentList.Add("-p");
            startInfo.ArgumentList.Add(request.ProjectName);
        }

        foreach (var arg in OperationArgs(request.Operation))
            startInfo.ArgumentList.Add(arg);

        // Resolved secrets (if any) go in as real process environment variables —
        // never as arguments — so a compose file's ${VAR} interpolation can see
        // them without the value ever appearing in `ps` output or our own logs.
        if (request.EnvironmentVariables is not null)
        {
            foreach (var (key, value) in request.EnvironmentVariables)
                startInfo.Environment[key] = value;
        }

        using var process = new Process { StartInfo = startInfo };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) stdout.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) stderr.AppendLine(e.Data); };

        try
        {
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            logger.LogError(ex, "Failed to start docker compose process for {WorkingDirectory}", request.WorkingDirectory);
            return new ComposeCommandResult(false, -1, stdout.ToString(), $"Failed to start docker: {ex.Message}");
        }

        return new ComposeCommandResult(process.ExitCode == 0, process.ExitCode, stdout.ToString(), stderr.ToString());
    }

    private static string[] OperationArgs(ComposeOperation operation) => operation switch
    {
        ComposeOperation.Up => ["up", "-d"],
        ComposeOperation.Down => ["down"],
        ComposeOperation.DownWithVolumes => ["down", "-v"],
        ComposeOperation.Restart => ["restart"],
        ComposeOperation.Start => ["start"],
        ComposeOperation.Stop => ["stop"],
        ComposeOperation.Ps => ["ps", "-a", "--format", "json"],
        _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, "Unsupported compose operation."),
    };
}
