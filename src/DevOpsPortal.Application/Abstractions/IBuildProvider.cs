using DevOpsPortal.Domain.Entities;
using DevOpsPortal.Domain.Enums;

namespace DevOpsPortal.Application.Abstractions;

/// <summary>What a build actually is right now, as reported by the provider —
/// distinct from BuildStatus, which also has to represent local-only states
/// (BuildStatus.Requested) that never come from the provider itself.</summary>
public enum ProviderBuildLifecycleState
{
    Queued,
    Running,
    Succeeded,
    Failed,
}

/// <summary>Result of triggering a build. Honest about the provider's real async
/// lifecycle (see BuildStatus doc comment): a successful trigger only ever
/// yields a queue reference, never a build number.</summary>
public record BuildTriggerResult(bool Success, string? ProviderQueueItemId, string? ErrorMessage)
{
    public static BuildTriggerResult Ok(string providerQueueItemId) => new(true, providerQueueItemId, null);
    public static BuildTriggerResult Fail(string message) => new(false, null, message);
}

public record BuildStatusResult(bool Success, ProviderBuildLifecycleState? State, int? BuildNumber, string? BuildUrl, string? ErrorMessage)
{
    public static BuildStatusResult Ok(ProviderBuildLifecycleState state, int? buildNumber, string? buildUrl) => new(true, state, buildNumber, buildUrl, null);
    public static BuildStatusResult Fail(string message) => new(false, null, null, null, message);
}

public record BuildLogResult(bool Success, string? LogText, bool IsComplete, string? ErrorMessage)
{
    public static BuildLogResult Ok(string logText, bool isComplete) => new(true, logText, isComplete, null);
    public static BuildLogResult Fail(string message) => new(false, null, false, message);
}

/// <summary>Branch/commit/parameters for a build trigger. JobName is always the
/// value already configured on the application's BuildConfiguration — never
/// caller-supplied — so this record intentionally has no way to name an
/// arbitrary job.</summary>
public record BuildTriggerRequest(string JobName, string? Branch, string? CommitSha, IReadOnlyDictionary<string, string> Parameters);

/// <summary>
/// A build provider (Jenkins today; the interface exists so the deployment
/// engine never depends on Jenkins specifically — master requirements §1).
/// Every method wraps a call that can legitimately fail (network, auth, the
/// provider being unreachable or misconfigured) without that being an
/// application error: callers inspect Success/ErrorMessage rather than
/// catching exceptions, mirroring IGitProviderClient's GitProviderResult
/// idiom. Implementations never throw for expected failure modes.
/// </summary>
public interface IBuildProvider
{
    BuildProviderType ProviderType { get; }

    bool IsConfigured(BuildServer buildServer);

    /// <summary>Triggers a build. Returns a queue reference, not a build number —
    /// see BuildStatusResult/GetBuildStatusAsync to resolve it.</summary>
    Task<BuildTriggerResult> TriggerBuildAsync(BuildServer buildServer, BuildTriggerRequest request, CancellationToken cancellationToken = default);

    /// <summary>Refreshes status for a build in progress. Pass knownBuildNumber once
    /// it's been resolved (cheaper, more precise); otherwise providerQueueItemId is
    /// used to attempt resolution. Safe to call repeatedly (refresh-on-read — no
    /// provider-side subscription/webhook exists).</summary>
    Task<BuildStatusResult> GetBuildStatusAsync(
        BuildServer buildServer, string jobName, string? providerQueueItemId, int? knownBuildNumber, CancellationToken cancellationToken = default);

    /// <summary>Live console log text for a resolved build. Never persisted by a
    /// caller — the provider remains the durable log store; this only proxies it,
    /// and callers must sanitize the result before returning it further (see
    /// LogSanitizer) since a build log can legitimately contain secrets that leaked
    /// into build output.</summary>
    Task<BuildLogResult> GetBuildLogAsync(BuildServer buildServer, string jobName, int buildNumber, CancellationToken cancellationToken = default);
}
