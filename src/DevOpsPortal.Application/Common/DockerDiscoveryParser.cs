using System.Globalization;
using System.Text.Json;
using DevOpsPortal.Application.Abstractions;
using DevOpsPortal.Domain.Enums;

namespace DevOpsPortal.Application.Common;

/// <summary>One container as discovered directly from `docker ps -aq` piped into
/// `docker inspect` — whole-server discovery, independent of any compose
/// project or configured ApplicationEnvironment (see
/// IRemoteExecutionProvider.DiscoverContainersAsync). `Stats` is filled in
/// separately from a `docker stats` snapshot, matched by container name.</summary>
public sealed record DiscoveredContainerRaw(
    string ContainerId,
    string Name,
    string Image,
    string? ImageTag,
    ContainerState State,
    string? DockerHealthStatus,
    DateTimeOffset? CreatedAt,
    DateTimeOffset? StartedAt,
    int RestartCount,
    IReadOnlyList<string> Ports,
    ContainerStatsInfo? Stats = null);

/// <summary>
/// Pure parsing for the Environment Infrastructure Dashboard's whole-server
/// discovery — no I/O, deliberately testable without SSH or a Docker daemon
/// (same principle as ContainerStateMapper). Every parse method is defensive:
/// malformed/unexpected input yields nulls or an empty result, never a thrown
/// exception that would turn a real connection into an apparent crash, and
/// never a fabricated value.
/// </summary>
public static class DockerDiscoveryParser
{
    /// <summary>Parses `docker inspect`'s JSON array output (one element per
    /// container) into a list, in whatever order Docker returned them. An
    /// empty/whitespace input or a `"[]"` (genuinely zero containers on the
    /// host) both correctly yield an empty list — that's a real state, not a
    /// parse failure.</summary>
    public static List<DiscoveredContainerRaw> ParseInspectArray(string rawJson)
    {
        var trimmed = rawJson.Trim();
        if (trimmed.Length == 0)
            return [];

        try
        {
            using var doc = JsonDocument.Parse(trimmed);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
                return [];

            var results = new List<DiscoveredContainerRaw>();
            foreach (var element in doc.RootElement.EnumerateArray())
            {
                if (ToContainer(element) is { } container)
                    results.Add(container);
            }
            return results;
        }
        catch (JsonException)
        {
            return [];
        }
    }

    /// <summary>Parses `docker stats --no-stream --format '{{json .}}'`'s
    /// newline-delimited JSON (one object per running container) into a
    /// lookup by container name — stopped containers simply have no entry,
    /// which callers treat as "no live stats" rather than a failure.</summary>
    public static Dictionary<string, ContainerStatsInfo> ParseStatsLines(string rawJson)
    {
        var result = new Dictionary<string, ContainerStatsInfo>(StringComparer.Ordinal);
        foreach (var line in rawJson.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            try
            {
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                    continue;

                var name = GetString(root, "Name");
                if (string.IsNullOrWhiteSpace(name))
                    continue;

                var (memUsage, memLimit) = SplitSlash(GetString(root, "MemUsage"));
                result[name] = new ContainerStatsInfo(
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
                // Skip this one line — one malformed stats line must never lose
                // every other container's data.
            }
        }
        return result;
    }

    /// <summary>Parses `/proc/loadavg`'s first three whitespace-separated fields
    /// (1/5/15-minute load averages). Anything else in the line is ignored.</summary>
    public static (double? Load1, double? Load5, double? Load15) ParseLoadAvg(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return (null, null, null);

        var parts = raw.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 3)
            return (null, null, null);

        return (
            ParseDouble(parts[0]),
            ParseDouble(parts[1]),
            ParseDouble(parts[2]));
    }

    /// <summary>Parses `free -b`'s "Mem:" line (exact byte counts — chosen over
    /// `free -h`'s unit-suffixed human output specifically so this doesn't need
    /// to guess at Ki/Mi/Gi suffix parsing).</summary>
    public static (long? TotalBytes, long? UsedBytes, long? AvailableBytes) ParseFreeBytes(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return (null, null, null);

        foreach (var line in raw.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 3 || !parts[0].StartsWith("Mem:", StringComparison.OrdinalIgnoreCase))
                continue;

            var total = ParseLong(parts[1]);
            var used = ParseLong(parts[2]);
            // "available" (accounts for reclaimable cache) is column index 6 when
            // present (a modern `free` with the shared/buff-cache/available
            // columns); fall back to "free" (index 3) on an older `free` build
            // that only reports total/used/free/shared.
            var available = parts.Length > 6 ? ParseLong(parts[6]) : parts.Length > 3 ? ParseLong(parts[3]) : null;
            return (total, used, available);
        }
        return (null, null, null);
    }

    /// <summary>Parses `df -Pk /`'s second (data) line — 1024-byte blocks, chosen
    /// for the same exact-value reason as `free -b` above. POSIX `-P` format is
    /// stable across distributions (always exactly: Filesystem, 1024-blocks,
    /// Used, Available, Capacity, Mounted-on).</summary>
    public static (long? TotalBytes, long? UsedBytes, long? AvailableBytes, double? UsePercent) ParseDfKb(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return (null, null, null, null);

        var lines = raw.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length < 2)
            return (null, null, null, null);

        var parts = lines[1].Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 5)
            return (null, null, null, null);

        var totalKb = ParseLong(parts[1]);
        var usedKb = ParseLong(parts[2]);
        var availableKb = ParseLong(parts[3]);
        var usePercent = double.TryParse(parts[4].TrimEnd('%'), NumberStyles.Number, CultureInfo.InvariantCulture, out var pct) ? pct : (double?)null;

        return (
            totalKb.HasValue ? totalKb * 1024 : null,
            usedKb.HasValue ? usedKb * 1024 : null,
            availableKb.HasValue ? availableKb * 1024 : null,
            usePercent);
    }

    private static DiscoveredContainerRaw? ToContainer(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
            return null;

        var id = GetString(root, "Id");
        var name = GetString(root, "Name")?.TrimStart('/');
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name))
            return null;

        var state = root.TryGetProperty("State", out var stateEl) && stateEl.ValueKind == JsonValueKind.Object ? stateEl : default;
        var dockerState = state.ValueKind == JsonValueKind.Object ? GetString(state, "Status") : null;
        var health = state.ValueKind == JsonValueKind.Object && state.TryGetProperty("Health", out var healthEl) && healthEl.ValueKind == JsonValueKind.Object
            ? GetString(healthEl, "Status")
            : null;
        var startedAt = ParseDockerTimestamp(state.ValueKind == JsonValueKind.Object ? GetString(state, "StartedAt") : null);
        var createdAt = ParseDockerTimestamp(GetString(root, "Created"));

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

        return new DiscoveredContainerRaw(
            ContainerId: id,
            Name: name,
            Image: image,
            ImageTag: tag,
            State: ContainerStateMapper.Map(dockerState, health),
            DockerHealthStatus: health,
            CreatedAt: createdAt,
            StartedAt: startedAt,
            RestartCount: restartCount,
            Ports: ports);
    }

    private static DateTimeOffset? ParseDockerTimestamp(string? raw) =>
        !string.IsNullOrWhiteSpace(raw) &&
        DateTimeOffset.TryParse(raw, null, DateTimeStyles.RoundtripKind, out var parsed) &&
        parsed.Year > 1
            ? parsed
            : null;

    private static double? ParsePercent(string? value) =>
        !string.IsNullOrWhiteSpace(value) && double.TryParse(value.TrimEnd('%'), NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;

    private static long? ParseLong(string? value) =>
        long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;

    private static double? ParseDouble(string? value) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;

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

    /// <summary>Same logic as DockerComposeContainerRuntimeProvider's own
    /// SplitImageTag — duplicated rather than shared because the two live in
    /// different layers (this is Application-layer pure logic; that one is
    /// Infrastructure-layer orchestration) and neither depends on the other.</summary>
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
}
