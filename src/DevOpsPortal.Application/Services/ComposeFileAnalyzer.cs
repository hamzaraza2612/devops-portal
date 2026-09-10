using DevOpsPortal.Application.Dtos.Discovery;
using YamlDotNet.Core;
using YamlDotNet.Serialization;

namespace DevOpsPortal.Application.Services;

public class ComposeFileAnalyzer : IComposeFileAnalyzer
{
    private const int MaxContentLength = 200_000;

    private static readonly string[] SecretKeyHints = ["PASSWORD", "SECRET", "TOKEN", "APIKEY", "API_KEY", "CONNECTIONSTRING", "PWD"];

    private readonly IDeserializer _deserializer = new DeserializerBuilder().Build();

    public ComposeAnalysisResult Analyze(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return new ComposeAnalysisResult([], ["Input is empty."]);
        if (content.Length > MaxContentLength)
            return new ComposeAnalysisResult([], [$"Input exceeds the {MaxContentLength:N0}-character limit for analysis."]);

        object? root;
        try
        {
            root = _deserializer.Deserialize<object>(content);
        }
        catch (YamlException ex)
        {
            return new ComposeAnalysisResult([], [$"Could not parse YAML: {ex.Message}"]);
        }

        var warnings = new List<string>();
        var rootMap = AsMap(root);
        if (rootMap is null || !rootMap.TryGetValue("services", out var servicesObj))
            return new ComposeAnalysisResult([], ["No top-level 'services:' section found."]);

        var servicesMap = AsMap(servicesObj);
        if (servicesMap is null || servicesMap.Count == 0)
            return new ComposeAnalysisResult([], ["'services:' section is empty."]);

        var topLevelNetworks = AsMap(rootMap.GetValueOrDefault("networks"));
        var externalNetworkNames = (topLevelNetworks ?? [])
            .Where(kv => IsTruthy(AsMap(kv.Value)?.GetValueOrDefault("external")))
            .Select(kv => kv.Key)
            .ToHashSet(StringComparer.Ordinal);

        var namedVolumeDevices = ResolveNamedVolumeDevices(AsMap(rootMap.GetValueOrDefault("volumes")));

        var services = new List<ComposeServiceSummary>();
        foreach (var (serviceName, serviceObj) in servicesMap)
        {
            var svc = AsMap(serviceObj);
            if (svc is null)
            {
                warnings.Add($"Service '{serviceName}' has no readable definition; skipped.");
                continue;
            }

            services.Add(BuildServiceSummary(serviceName, svc, externalNetworkNames, namedVolumeDevices, warnings));
        }

        return new ComposeAnalysisResult(services, warnings);
    }

    private static ComposeServiceSummary BuildServiceSummary(
        string serviceName,
        Dictionary<string, object?> svc,
        HashSet<string> externalNetworkNames,
        Dictionary<string, string?> namedVolumeDevices,
        List<string> warnings)
    {
        var image = AsString(svc.GetValueOrDefault("image"));
        var containerName = AsString(svc.GetValueOrDefault("container_name"));
        var workingDir = AsString(svc.GetValueOrDefault("working_dir"));
        var restart = AsString(svc.GetValueOrDefault("restart"));

        var ports = AsStringList(svc.GetValueOrDefault("ports"));
        var entrypoint = AsStringOrSpaceList(svc.GetValueOrDefault("entrypoint"));
        var envEntries = AsEnvironmentEntries(svc.GetValueOrDefault("environment")).Select(Redact).ToList();
        var extraHosts = AsStringOrMapEntries(svc.GetValueOrDefault("extra_hosts"));
        var networks = AsNetworkNames(svc.GetValueOrDefault("networks"));
        var volumes = AsVolumes(svc.GetValueOrDefault("volumes"), namedVolumeDevices);

        if (containerName is null)
            warnings.Add($"Service '{serviceName}' has no explicit container_name — Compose would generate one; set it explicitly when configuring.");
        if (image is null)
            warnings.Add($"Service '{serviceName}' has no 'image'.");

        var usesExternal = networks.Any(externalNetworkNames.Contains);

        return new ComposeServiceSummary(
            serviceName, image, containerName, ports, volumes, envEntries, workingDir, entrypoint, extraHosts, networks, usesExternal, restart);
    }

    private static string Redact(string environmentEntry)
    {
        var idx = environmentEntry.IndexOf('=');
        if (idx <= 0)
            return environmentEntry;

        var key = environmentEntry[..idx];
        return SecretKeyHints.Any(hint => key.Contains(hint, StringComparison.OrdinalIgnoreCase))
            ? $"{key}=***REDACTED***"
            : environmentEntry;
    }

    private static Dictionary<string, string?> ResolveNamedVolumeDevices(Dictionary<string, object?>? topLevelVolumes)
    {
        var result = new Dictionary<string, string?>(StringComparer.Ordinal);
        if (topLevelVolumes is null)
            return result;

        foreach (var (name, def) in topLevelVolumes)
        {
            var driverOpts = AsMap(AsMap(def)?.GetValueOrDefault("driver_opts"));
            result[name] = AsString(driverOpts?.GetValueOrDefault("device"));
        }

        return result;
    }

    private static bool IsTruthy(object? value) => value switch
    {
        bool b => b,
        string s => bool.TryParse(s, out var parsed) && parsed,
        _ => false,
    };

    // --- YAML tree helpers: YamlDotNet's untyped deserialization returns
    // Dictionary<object,object> / List<object> / scalar (string/bool/...) nodes. ---

    private static Dictionary<string, object?>? AsMap(object? node) =>
        node is Dictionary<object, object> raw
            ? raw.ToDictionary(kv => kv.Key?.ToString() ?? string.Empty, object? (kv) => kv.Value)
            : null;

    private static string? AsString(object? node) => node?.ToString();

    private static List<string> AsStringList(object? node) => node switch
    {
        List<object> list => list.Select(x => x?.ToString() ?? string.Empty).ToList(),
        null => [],
        _ => [node.ToString() ?? string.Empty],
    };

    private static List<string> AsStringOrSpaceList(object? node) => node switch
    {
        List<object> list => list.Select(x => x?.ToString() ?? string.Empty).ToList(),
        string s => s.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList(),
        null => [],
        _ => [node.ToString() ?? string.Empty],
    };

    private static List<string> AsEnvironmentEntries(object? node) => node switch
    {
        List<object> list => list.Select(x => x?.ToString() ?? string.Empty).ToList(),
        Dictionary<object, object> map => map.Select(kv => $"{kv.Key}={kv.Value}").ToList(),
        _ => [],
    };

    private static List<string> AsStringOrMapEntries(object? node) => node switch
    {
        List<object> list => list.Select(x => x?.ToString() ?? string.Empty).ToList(),
        Dictionary<object, object> map => map.Select(kv => $"{kv.Key}:{kv.Value}").ToList(),
        _ => [],
    };

    private static List<string> AsNetworkNames(object? node) => node switch
    {
        List<object> list => list.Select(x => x?.ToString() ?? string.Empty).ToList(),
        Dictionary<object, object> map => map.Keys.Select(k => k?.ToString() ?? string.Empty).ToList(),
        _ => [],
    };

    private static List<ComposeVolumeSummary> AsVolumes(object? node, Dictionary<string, string?> namedVolumeDevices)
    {
        if (node is not List<object> list)
            return [];

        var result = new List<ComposeVolumeSummary>();
        foreach (var item in list)
        {
            if (item is string s)
            {
                var parts = s.Split(':', 3);
                if (parts.Length < 2)
                    continue;

                result.Add(BuildVolumeSummary(parts[0], parts[1], namedVolumeDevices));
            }
            else if (item is Dictionary<object, object> raw)
            {
                var map = AsMap(raw)!;
                var target = AsString(map.GetValueOrDefault("target")) ?? string.Empty;
                var source = AsString(map.GetValueOrDefault("source"));
                var type = AsString(map.GetValueOrDefault("type"));
                var isBind = type == "bind" || (source?.StartsWith('/') ?? false);
                result.Add(new ComposeVolumeSummary(source, target, isBind, LooksLikePublishBinding(source, target)));
            }
        }

        return result;
    }

    private static ComposeVolumeSummary BuildVolumeSummary(string source, string target, Dictionary<string, string?> namedVolumeDevices)
    {
        var isPathSource = source.StartsWith('/') || source.StartsWith('.');
        if (isPathSource)
            return new ComposeVolumeSummary(source, target, true, LooksLikePublishBinding(source, target));

        // Named volume — resolve against top-level `volumes:` to see if it's
        // actually a host bind mount in disguise (the DmsApi pattern).
        if (namedVolumeDevices.TryGetValue(source, out var device) && device is not null)
            return new ComposeVolumeSummary(device, target, true, LooksLikePublishBinding(device, target));

        return new ComposeVolumeSummary(source, target, false, false);
    }

    private static bool LooksLikePublishBinding(string? source, string target) =>
        (source?.Contains("publish", StringComparison.OrdinalIgnoreCase) ?? false) ||
        target.Equals("/app", StringComparison.OrdinalIgnoreCase);
}
