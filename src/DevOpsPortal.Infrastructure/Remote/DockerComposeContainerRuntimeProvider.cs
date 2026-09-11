using System.Text.Json;
using DevOpsPortal.Application.Abstractions;
using DevOpsPortal.Application.Common;
using DevOpsPortal.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace DevOpsPortal.Infrastructure.Remote;

/// <summary>
/// Docker/Compose-domain orchestration on top of <see cref="IRemoteExecutionProvider"/>:
/// discovers a target server's containers for a compose project (`compose ps`),
/// enriches each with `docker inspect` for the fields `ps` doesn't reliably
/// carry, and maps Docker's own state/health vocabulary via
/// <see cref="ContainerStateMapper"/>. Always checks
/// <see cref="IRemoteExecutionProvider.IsConfigured"/> first and short-circuits
/// to <c>IsReachable: false</c> without attempting anything if the target
/// server can't be reached — with only <c>NotConfiguredRemoteExecutionProvider</c>
/// registered today, that is every call, every target server. This class's own
/// parsing/orchestration logic is still fully real and tested (see
/// DockerComposeContainerRuntimeProviderTests using a fake
/// IRemoteExecutionProvider) so it is ready the moment a real provider is
/// plugged in — nothing here needs to change when that happens.
/// </summary>
public class DockerComposeContainerRuntimeProvider(IRemoteExecutionProvider remoteExecutionProvider, ILogger<DockerComposeContainerRuntimeProvider> logger)
    : IContainerRuntimeProvider
{
    public async Task<ContainerRuntimeStatusResult> GetStatusAsync(
        TargetServer targetServer, string workingDirectory, string composeFilePath, string? projectName, CancellationToken cancellationToken = default)
    {
        if (!remoteExecutionProvider.IsConfigured(targetServer))
            return new ContainerRuntimeStatusResult(false, UnreachableReason(targetServer), []);

        var psResult = await remoteExecutionProvider.RunComposeAsync(
            targetServer, new ComposeCommandRequest(workingDirectory, composeFilePath, projectName, ComposeOperation.Ps), cancellationToken);
        if (!psResult.Success)
        {
            logger.LogInformation(
                "docker compose ps returned no usable status for '{WorkingDirectory}' on target server '{TargetServerName}': {Error}",
                workingDirectory, targetServer.Name, psResult.StandardError);
            return new ContainerRuntimeStatusResult(true, psResult.StandardError, []);
        }

        var discovered = ParsePsOutput(psResult.StandardOutput);
        if (discovered.Count == 0)
            return new ContainerRuntimeStatusResult(true, null, []);

        var results = new List<ContainerStatusInfo>(discovered.Count);
        foreach (var container in discovered)
        {
            var inspectResult = await remoteExecutionProvider.InspectContainerAsync(targetServer, container.Name, cancellationToken);
            var inspected = inspectResult.Success ? ParseInspectOutput(inspectResult.RawJson) : null;

            // A stats snapshot is independent of inspect succeeding — a container
            // that just started (no stats yet) or a transient stats failure must
            // never hide the state/health/restart-count data inspect already
            // provided.
            var statsResult = await remoteExecutionProvider.GetContainerStatsAsync(targetServer, container.Name, cancellationToken);
            var stats = statsResult.Success ? ParseStatsOutput(statsResult.RawJson) : null;

            results.Add((inspected ?? FallbackFromPs(container)) with { Stats = stats });
        }

        return new ContainerRuntimeStatusResult(true, null, results);
    }

    public async Task<ContainerLogsResult> GetLogsAsync(
        TargetServer targetServer, string workingDirectory, string composeFilePath, string? projectName, string containerName, int tailLines,
        CancellationToken cancellationToken = default)
    {
        if (!remoteExecutionProvider.IsConfigured(targetServer))
            return new ContainerLogsResult(false, false, string.Empty, UnreachableReason(targetServer));

        // Re-discover this project's actual containers and only fetch logs for a
        // name that's really one of them — never trust a caller-supplied
        // containerName blindly (see GetLogsAsync's own doc comment on
        // IContainerRuntimeProvider for why).
        var psResult = await remoteExecutionProvider.RunComposeAsync(
            targetServer, new ComposeCommandRequest(workingDirectory, composeFilePath, projectName, ComposeOperation.Ps), cancellationToken);
        if (!psResult.Success)
            return new ContainerLogsResult(true, false, string.Empty, string.IsNullOrWhiteSpace(psResult.StandardError) ? "Could not list containers for this application environment." : psResult.StandardError);

        var discovered = ParsePsOutput(psResult.StandardOutput);
        if (!discovered.Any(c => c.Name == containerName))
            return new ContainerLogsResult(true, false, string.Empty, $"'{containerName}' is not a container of this application environment.");

        var logsResult = await remoteExecutionProvider.GetContainerLogsAsync(targetServer, containerName, tailLines, cancellationToken);
        return new ContainerLogsResult(true, logsResult.Success, logsResult.Logs, logsResult.Error);
    }

    public async Task<ContainerRuntimeOperationResult> RunOperationAsync(
        TargetServer targetServer, string workingDirectory, string composeFilePath, string? projectName, ComposeOperation operation,
        CancellationToken cancellationToken = default)
    {
        if (!remoteExecutionProvider.IsConfigured(targetServer))
            return new ContainerRuntimeOperationResult(false, false, UnreachableReason(targetServer));

        var result = await remoteExecutionProvider.RunComposeAsync(
            targetServer, new ComposeCommandRequest(workingDirectory, composeFilePath, projectName, operation), cancellationToken);
        var detail = $"exit {result.ExitCode}: {(result.Success ? result.StandardOutput : result.StandardError)}".Trim();
        return new ContainerRuntimeOperationResult(true, result.Success, detail);
    }

    private static string UnreachableReason(TargetServer targetServer) =>
        $"No remote execution mechanism is configured for target server '{targetServer.Name}'.";

    private static ContainerStatusInfo FallbackFromPs(PsEntry entry)
    {
        var (image, tag) = SplitImageTag(entry.Image);
        return new ContainerStatusInfo(
            entry.Service, entry.Name, image, tag,
            ContainerStateMapper.Map(entry.State, entry.Health), entry.Health, null, 0, []);
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

    /// <summary>Parses one `docker stats --no-stream --format '{{json .}}'` line.
    /// Docker's own stats JSON already pre-formats CPUPerc/MemPerc as
    /// percentage strings ("0.42%") and MemUsage/NetIO/BlockIO as human-readable
    /// "value / value" strings ("12MiB / 1.952GiB") — CPU/Mem percentages are
    /// parsed to numbers here since that's a trivial, safe strip-the-%-and-parse;
    /// MemUsage's two sides are split into MemoryUsage/MemoryLimit as separately
    /// requested fields, kept as Docker's own strings rather than re-derived into
    /// exact byte counts (see ContainerStatsInfo's doc comment).</summary>
    private static ContainerStatsInfo? ParseStatsOutput(string rawOutput)
    {
        var trimmed = rawOutput.Trim();
        if (trimmed.Length == 0)
            return null;

        try
        {
            using var doc = JsonDocument.Parse(trimmed);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return null;

            var (memUsage, memLimit) = SplitSlash(GetString(root, "MemUsage"));

            return new ContainerStatsInfo(
                CpuPercent: ParsePercent(GetString(root, "CPUPerc")),
                MemoryUsage: memUsage,
                MemoryLimit: memLimit,
                MemoryPercent: ParsePercent(GetString(root, "MemPerc")),
                NetworkIO: GetString(root, "NetIO"),
                BlockIO: GetString(root, "BlockIO"),
                PidCount: int.TryParse(GetString(root, "PIDs"), out var pids) ? pids : null);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static double? ParsePercent(string? value) =>
        !string.IsNullOrWhiteSpace(value) && double.TryParse(value.TrimEnd('%'), System.Globalization.CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;

    private static (string? Left, string? Right) SplitSlash(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return (null, null);

        var parts = value.Split('/', 2, StringSplitOptions.TrimEntries);
        return parts.Length == 2 ? (parts[0], parts[1]) : (value.Trim(), null);
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
