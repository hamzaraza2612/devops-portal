using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DevOpsPortal.Application.Abstractions;
using DevOpsPortal.Domain.Entities;
using DevOpsPortal.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace DevOpsPortal.Infrastructure.Build;

/// <summary>
/// Calls the real Jenkins REST API over HTTP — this is operational as soon as
/// a BuildServer is configured with a reachable BaseUrl, unlike Phase 5's
/// remote Docker gap (there is no socket/SSH problem here: Jenkins is reached
/// the same way any other HTTP API is). Authentication is HTTP Basic using
/// BuildServer.Username plus a token read from the environment variable named
/// by BuildServer.ApiTokenEnvVarName at call time — never stored, logged, or
/// returned. Models Jenkins's real two-phase async lifecycle honestly:
/// triggering a job returns a queue item reference (from the Location header
/// of the 201 response), never a build number; the build number is only
/// knowable once the queue item resolves to an "executable". All failure
/// modes (network, auth, 404, malformed response) are returned as a Fail
/// result — this never throws for expected failures.
/// </summary>
public class JenkinsBuildProvider(HttpClient httpClient, ILogger<JenkinsBuildProvider> logger) : IBuildProvider
{
    public BuildProviderType ProviderType => BuildProviderType.Jenkins;

    public bool IsConfigured(BuildServer buildServer) =>
        buildServer.ProviderType == BuildProviderType.Jenkins &&
        Uri.TryCreate(buildServer.BaseUrl, UriKind.Absolute, out _);

    public async Task<BuildTriggerResult> TriggerBuildAsync(
        BuildServer buildServer, BuildTriggerRequest request, CancellationToken cancellationToken = default)
    {
        if (!IsConfigured(buildServer))
            return BuildTriggerResult.Fail($"Build server '{buildServer.Name}' does not have a valid BaseUrl configured.");
        if (string.IsNullOrWhiteSpace(request.JobName))
            return BuildTriggerResult.Fail("JobName is required to trigger a build.");

        try
        {
            var jobUrl = BuildJobUrl(buildServer.BaseUrl, request.JobName);
            var triggerUrl = request.Parameters.Count > 0
                ? $"{jobUrl}/buildWithParameters?{BuildQueryString(request.Parameters)}"
                : $"{jobUrl}/build";

            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, triggerUrl);
            ApplyAuth(buildServer, httpRequest);
            await ApplyCrumbAsync(buildServer, httpRequest, cancellationToken);

            using var response = await httpClient.SendAsync(httpRequest, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Jenkins trigger for job {JobName} on {BuildServer} returned {StatusCode}", request.JobName, buildServer.Name, response.StatusCode);
                return BuildTriggerResult.Fail($"Jenkins returned {(int)response.StatusCode} {response.ReasonPhrase} triggering job '{request.JobName}'.");
            }

            var location = response.Headers.Location?.ToString();
            var queueItemId = ExtractQueueItemId(location);
            return queueItemId is null
                ? BuildTriggerResult.Fail("Jenkins accepted the trigger but did not return a queue item location.")
                : BuildTriggerResult.Ok(queueItemId);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(ex, "Jenkins trigger failed for job {JobName} on {BuildServer}", request.JobName, buildServer.Name);
            return BuildTriggerResult.Fail($"Could not reach build server '{buildServer.Name}'.");
        }
    }

    public async Task<BuildStatusResult> GetBuildStatusAsync(
        BuildServer buildServer, string jobName, string? providerQueueItemId, int? knownBuildNumber, CancellationToken cancellationToken = default)
    {
        if (!IsConfigured(buildServer))
            return BuildStatusResult.Fail($"Build server '{buildServer.Name}' does not have a valid BaseUrl configured.");

        try
        {
            if (knownBuildNumber is { } buildNumber)
                return await GetBuildStatusByNumberAsync(buildServer, jobName, buildNumber, cancellationToken);

            if (string.IsNullOrWhiteSpace(providerQueueItemId))
                return BuildStatusResult.Fail("Neither a build number nor a queue item id is known for this build.");

            return await ResolveQueueItemAsync(buildServer, jobName, providerQueueItemId, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            logger.LogWarning(ex, "Jenkins status refresh failed for job {JobName} on {BuildServer}", jobName, buildServer.Name);
            return BuildStatusResult.Fail($"Could not reach build server '{buildServer.Name}'.");
        }
    }

    public async Task<BuildLogResult> GetBuildLogAsync(
        BuildServer buildServer, string jobName, int buildNumber, CancellationToken cancellationToken = default)
    {
        if (!IsConfigured(buildServer))
            return BuildLogResult.Fail($"Build server '{buildServer.Name}' does not have a valid BaseUrl configured.");

        try
        {
            var jobUrl = BuildJobUrl(buildServer.BaseUrl, jobName);

            var isComplete = false;
            using (var statusRequest = new HttpRequestMessage(HttpMethod.Get, $"{jobUrl}/{buildNumber}/api/json?tree=building"))
            {
                ApplyAuth(buildServer, statusRequest);
                using var statusResponse = await httpClient.SendAsync(statusRequest, cancellationToken);
                if (statusResponse.IsSuccessStatusCode)
                {
                    var status = await statusResponse.Content.ReadFromJsonAsync<JenkinsBuildStatusDto>(cancellationToken);
                    isComplete = status is { Building: false };
                }
            }

            using var logRequest = new HttpRequestMessage(HttpMethod.Get, $"{jobUrl}/{buildNumber}/consoleText");
            ApplyAuth(buildServer, logRequest);
            using var logResponse = await httpClient.SendAsync(logRequest, cancellationToken);
            if (!logResponse.IsSuccessStatusCode)
                return BuildLogResult.Fail($"Jenkins returned {(int)logResponse.StatusCode} {logResponse.ReasonPhrase} fetching the console log.");

            var logText = await logResponse.Content.ReadAsStringAsync(cancellationToken);
            return BuildLogResult.Ok(logText, isComplete);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(ex, "Jenkins log fetch failed for job {JobName} build {BuildNumber} on {BuildServer}", jobName, buildNumber, buildServer.Name);
            return BuildLogResult.Fail($"Could not reach build server '{buildServer.Name}'.");
        }
    }

    // ------------------------------------------------------------- internals

    private async Task<BuildStatusResult> GetBuildStatusByNumberAsync(BuildServer buildServer, string jobName, int buildNumber, CancellationToken cancellationToken)
    {
        var jobUrl = BuildJobUrl(buildServer.BaseUrl, jobName);
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{jobUrl}/{buildNumber}/api/json?tree=building,result,url");
        ApplyAuth(buildServer, request);

        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            return BuildStatusResult.Fail($"Jenkins returned {(int)response.StatusCode} {response.ReasonPhrase} fetching build #{buildNumber}.");

        var dto = await response.Content.ReadFromJsonAsync<JenkinsBuildStatusDto>(cancellationToken);
        if (dto is null)
            return BuildStatusResult.Fail($"Jenkins returned an empty response for build #{buildNumber}.");

        var state = MapResult(dto.Building, dto.Result);
        return BuildStatusResult.Ok(state, buildNumber, dto.Url);
    }

    private async Task<BuildStatusResult> ResolveQueueItemAsync(BuildServer buildServer, string jobName, string queueItemId, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{buildServer.BaseUrl}/queue/item/{Uri.EscapeDataString(queueItemId)}/api/json");
        ApplyAuth(buildServer, request);

        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            return BuildStatusResult.Fail($"Jenkins returned {(int)response.StatusCode} {response.ReasonPhrase} fetching queue item '{queueItemId}'.");

        var dto = await response.Content.ReadFromJsonAsync<JenkinsQueueItemDto>(cancellationToken);
        if (dto is null)
            return BuildStatusResult.Fail($"Jenkins returned an empty response for queue item '{queueItemId}'.");

        if (dto.Cancelled == true && dto.Executable is null)
            return BuildStatusResult.Fail("The build was cancelled while queued.");

        if (dto.Executable is null)
            return BuildStatusResult.Ok(ProviderBuildLifecycleState.Queued, null, null);

        // The queue item just resolved to a real build — follow up with its own
        // status so callers see Running/Succeeded/Failed immediately rather than
        // waiting for a second poll.
        return await GetBuildStatusByNumberAsync(buildServer, jobName, dto.Executable.Number, cancellationToken);
    }

    private static ProviderBuildLifecycleState MapResult(bool building, string? result) => (building, result?.ToUpperInvariant()) switch
    {
        (true, _) => ProviderBuildLifecycleState.Running,
        (false, "SUCCESS") => ProviderBuildLifecycleState.Succeeded,
        (false, "FAILURE" or "ABORTED" or "UNSTABLE") => ProviderBuildLifecycleState.Failed,
        (false, null) => ProviderBuildLifecycleState.Running,
        _ => ProviderBuildLifecycleState.Failed,
    };

    private static string BuildJobUrl(string baseUrl, string jobName)
    {
        var segments = jobName.Split('/', StringSplitOptions.RemoveEmptyEntries).Select(Uri.EscapeDataString);
        return $"{baseUrl.TrimEnd('/')}/job/{string.Join("/job/", segments)}";
    }

    private static string BuildQueryString(IReadOnlyDictionary<string, string> parameters) =>
        string.Join('&', parameters.Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}"));

    private static string? ExtractQueueItemId(string? location)
    {
        if (string.IsNullOrWhiteSpace(location))
            return null;

        var segments = location.TrimEnd('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        var queueIndex = Array.LastIndexOf(segments, "item");
        return queueIndex >= 0 && queueIndex > 0 && segments[queueIndex - 1] == "queue" ? segments[queueIndex + 1] : null;
    }

    private static void ApplyAuth(BuildServer buildServer, HttpRequestMessage request)
    {
        if (string.IsNullOrWhiteSpace(buildServer.Username) || string.IsNullOrWhiteSpace(buildServer.ApiTokenEnvVarName))
            return;

        var token = Environment.GetEnvironmentVariable(buildServer.ApiTokenEnvVarName);
        if (string.IsNullOrWhiteSpace(token))
            return;

        var bytes = Encoding.UTF8.GetBytes($"{buildServer.Username}:{token}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(bytes));
    }

    /// <summary>Jenkins CSRF protection (crumbs) is commonly enabled; fetch one and
    /// attach it if the endpoint exists, but tolerate its absence (disabled crumb
    /// issuer, or a Jenkins reverse-proxied without it exposed) rather than
    /// failing the whole request over it.</summary>
    private async Task ApplyCrumbAsync(BuildServer buildServer, HttpRequestMessage request, CancellationToken cancellationToken)
    {
        try
        {
            using var crumbRequest = new HttpRequestMessage(HttpMethod.Get, $"{buildServer.BaseUrl.TrimEnd('/')}/crumbIssuer/api/json");
            ApplyAuth(buildServer, crumbRequest);
            using var crumbResponse = await httpClient.SendAsync(crumbRequest, cancellationToken);
            if (!crumbResponse.IsSuccessStatusCode)
                return;

            var crumb = await crumbResponse.Content.ReadFromJsonAsync<JenkinsCrumbDto>(cancellationToken);
            if (crumb is { Crumb: not null, CrumbRequestField: not null })
                request.Headers.Add(crumb.CrumbRequestField, crumb.Crumb);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            logger.LogDebug(ex, "Jenkins crumb issuer unreachable for {BuildServer} — proceeding without CSRF crumb.", buildServer.Name);
        }
    }

    private sealed class JenkinsCrumbDto
    {
        [JsonPropertyName("crumb")]
        public string? Crumb { get; set; }

        [JsonPropertyName("crumbRequestField")]
        public string? CrumbRequestField { get; set; }
    }

    private sealed class JenkinsQueueItemDto
    {
        [JsonPropertyName("cancelled")]
        public bool? Cancelled { get; set; }

        [JsonPropertyName("executable")]
        public JenkinsExecutableDto? Executable { get; set; }
    }

    private sealed class JenkinsExecutableDto
    {
        [JsonPropertyName("number")]
        public int Number { get; set; }

        [JsonPropertyName("url")]
        public string? Url { get; set; }
    }

    private sealed class JenkinsBuildStatusDto
    {
        [JsonPropertyName("building")]
        public bool Building { get; set; }

        [JsonPropertyName("result")]
        public string? Result { get; set; }

        [JsonPropertyName("url")]
        public string? Url { get; set; }
    }
}
