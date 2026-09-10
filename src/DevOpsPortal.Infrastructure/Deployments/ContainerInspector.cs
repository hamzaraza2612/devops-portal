using System.Diagnostics;
using System.Text;
using System.Text.Json;
using DevOpsPortal.Application.Abstractions;
using DevOpsPortal.Application.Common;
using DevOpsPortal.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace DevOpsPortal.Infrastructure.Deployments;

/// <summary>
/// Two-step, read-only container inspection: (1) `docker compose ps -a --format
/// json` — scoped entirely to the already-validated working directory/compose
/// file, discovers this project's own container names; (2) `docker inspect
/// &lt;name&gt;` per discovered name for the rich fields `compose ps` doesn't
/// reliably carry (restart count, exact start time, native health status).
/// Container names passed to step 2 are never caller input — they only ever
/// come back from this instance's own step-1 call, so there is no path from a
/// request to an arbitrary container name. Both steps use `Process` with
/// `ArgumentList` exclusively, matching ComposeCommandExecutor. Never throws
/// for an expected failure (project not found, Docker unreachable, malformed
/// output) — returns as much as could be determined, defaulting to an empty
/// list or ContainerState.Unknown rather than propagating an exception into a
/// status page.
/// </summary>
public class ContainerInspector(IComposeCommandExecutor composeExecutor, ILogger<ContainerInspector> logger) : IContainerInspector
{
    public async Task<IReadOnlyList<ContainerStatusInfo>> GetStatusAsync(
        string workingDirectory, string composeFilePath, string? projectName, CancellationToken cancellationToken = default)
    {
        var psResult = await composeExecutor.RunAsync(
            new ComposeCommandRequest(workingDirectory, composeFilePath, projectName, ComposeOperation.Ps), cancellationToken);

        if (!psResult.Success)
        {
            logger.LogInformation(
                "docker compose ps returned no usable status for '{WorkingDirectory}': {Error}",
                workingDirectory, psResult.StandardError);
            return [];
        }

        var discovered = ParsePsOutput(psResult.StandardOutput);
        if (discovered.Count == 0)
            return [];

        var results = new List<ContainerStatusInfo>(discovered.Count);
        foreach (var container in discovered)
        {
            var inspected = await InspectContainerAsync(container.Name, cancellationToken);
            results.Add(inspected ?? FallbackFromPs(container));
        }

        return results;
    }

    private static ContainerStatusInfo FallbackFromPs(PsEntry entry)
    {
        var (image, tag) = SplitImageTag(entry.Image);
        return new ContainerStatusInfo(
            entry.Service, entry.Name, image, tag,
            ContainerStateMapper.Map(entry.State, entry.Health), entry.Health, null, 0, []);
    }

    private async Task<ContainerStatusInfo?> InspectContainerAsync(string containerName, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "docker",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("inspect");
        startInfo.ArgumentList.Add(containerName);

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
            logger.LogWarning(ex, "Failed to start docker inspect for container {ContainerName}", containerName);
            return null;
        }

        if (process.ExitCode != 0)
        {
            logger.LogInformation("docker inspect {ContainerName} exited {ExitCode}: {Error}", containerName, process.ExitCode, stderr.ToString());
            return null;
        }

        return ParseInspectOutput(stdout.ToString());
    }

    /// <summary>`docker compose ps --format json` output has varied across Compose
    /// versions between one JSON array on a single line and newline-delimited JSON
    /// objects — this tries the array form first, then falls back to NDJSON,
    /// skipping any line that isn't valid JSON rather than failing the whole call.</summary>
    private List<PsEntry> ParsePsOutput(string rawOutput)
    {
        var trimmed = rawOutput.Trim();
        if (trimmed.Length == 0)
            return [];

        if (trimmed.StartsWith('['))
        {
            try
            {
                using var doc = JsonDocument.Parse(trimmed);
                return doc.RootElement.EnumerateArray().Select(ToPsEntry).OfType<PsEntry>().ToList();
            }
            catch (JsonException)
            {
                // Fall through to line-by-line parsing below.
            }
        }

        var entries = new List<PsEntry>();
        foreach (var line in trimmed.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            try
            {
                using var doc = JsonDocument.Parse(line);
                if (ToPsEntry(doc.RootElement) is { } entry)
                    entries.Add(entry);
            }
            catch (JsonException ex)
            {
                logger.LogDebug(ex, "Skipping unparsable docker compose ps line.");
            }
        }
        return entries;
    }

    private static PsEntry? ToPsEntry(JsonElement element)
    {
        var name = GetString(element, "Name");
        if (string.IsNullOrWhiteSpace(name))
            return null;

        return new PsEntry(
            name,
            GetString(element, "Service") ?? name,
            GetString(element, "Image") ?? string.Empty,
            GetString(element, "State"),
            GetString(element, "Health"));
    }

    private static ContainerStatusInfo? ParseInspectOutput(string rawOutput)
    {
        var trimmed = rawOutput.Trim();
        if (trimmed.Length == 0)
            return null;

        try
        {
            using var doc = JsonDocument.Parse(trimmed);
            // `docker inspect` always returns a JSON array, even for one target.
            var root = doc.RootElement.ValueKind == JsonValueKind.Array
                ? doc.RootElement.EnumerateArray().FirstOrDefault()
                : doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return null;

            var name = GetString(root, "Name")?.TrimStart('/') ?? string.Empty;
            var state = root.TryGetProperty("State", out var stateEl) ? stateEl : default;
            var dockerState = state.ValueKind == JsonValueKind.Object ? GetString(state, "Status") : null;
            var startedAtRaw = state.ValueKind == JsonValueKind.Object ? GetString(state, "StartedAt") : null;
            var health = state.ValueKind == JsonValueKind.Object && state.TryGetProperty("Health", out var healthEl) && healthEl.ValueKind == JsonValueKind.Object
                ? GetString(healthEl, "Status")
                : null;

            DateTimeOffset? startedAt = null;
            if (!string.IsNullOrWhiteSpace(startedAtRaw) &&
                DateTimeOffset.TryParse(startedAtRaw, null, System.Globalization.DateTimeStyles.RoundtripKind, out var parsed) &&
                parsed.Year > 1)
            {
                startedAt = parsed;
            }

            var restartCount = root.TryGetProperty("RestartCount", out var restartEl) && restartEl.ValueKind == JsonValueKind.Number
                ? restartEl.GetInt32()
                : 0;

            var configImage = root.TryGetProperty("Config", out var configEl) && configEl.ValueKind == JsonValueKind.Object
                ? GetString(configEl, "Image")
                : null;
            var (image, tag) = SplitImageTag(configImage ?? string.Empty);

            var ports = new List<string>();
            if (root.TryGetProperty("NetworkSettings", out var netEl) && netEl.ValueKind == JsonValueKind.Object &&
                netEl.TryGetProperty("Ports", out var portsEl) && portsEl.ValueKind == JsonValueKind.Object)
            {
                foreach (var portProp in portsEl.EnumerateObject())
                {
                    if (portProp.Value.ValueKind != JsonValueKind.Array || portProp.Value.GetArrayLength() == 0)
                    {
                        ports.Add(portProp.Name);
                        continue;
                    }

                    foreach (var binding in portProp.Value.EnumerateArray())
                    {
                        var hostPort = GetString(binding, "HostPort");
                        ports.Add(string.IsNullOrWhiteSpace(hostPort) ? portProp.Name : $"{hostPort}->{portProp.Name}");
                    }
                }
            }

            return new ContainerStatusInfo(
                ServiceName: name,
                ContainerName: name,
                Image: image,
                ImageTag: tag,
                State: ContainerStateMapper.Map(dockerState, health),
                DockerHealthStatus: health,
                StartedAt: startedAt,
                RestartCount: restartCount,
                Ports: ports);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? GetString(JsonElement element, string propertyName) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    /// <summary>Splits "registry.example.com:5000/group/app:1.2.3" into
    /// ("registry.example.com:5000/group/app", "1.2.3") — only the segment after
    /// the last '/' is checked for a ':' tag separator, so a registry host:port
    /// is never mistaken for an image tag.</summary>
    private static (string Image, string? Tag) SplitImageTag(string imageRef)
    {
        if (string.IsNullOrWhiteSpace(imageRef))
            return (string.Empty, null);

        var lastSlash = imageRef.LastIndexOf('/');
        var lastSegment = lastSlash >= 0 ? imageRef[(lastSlash + 1)..] : imageRef;
        var colonInSegment = lastSegment.LastIndexOf(':');
        if (colonInSegment < 0)
            return (imageRef, null);

        var tagStart = (lastSlash >= 0 ? lastSlash + 1 : 0) + colonInSegment;
        return (imageRef[..tagStart], imageRef[(tagStart + 1)..]);
    }

    private sealed record PsEntry(string Name, string Service, string Image, string? State, string? Health);
}
