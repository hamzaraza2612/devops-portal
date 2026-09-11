using DevOpsPortal.Application.Abstractions;
using DevOpsPortal.Infrastructure.Email;
using DevOpsPortal.Infrastructure.Remote;
using Microsoft.Extensions.Options;
using HealthCheckResult = Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult;
using IHealthCheck = Microsoft.Extensions.Diagnostics.HealthChecks.IHealthCheck;
using HealthCheckContext = Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckContext;

namespace DevOpsPortal.Infrastructure.Integrations;

/// <summary>
/// Reports the configuration state of the portal's optional external
/// integrations — never a live network call (SMTP/GitLab reachability would
/// make every health check slow and flaky), just "is this wired up at all".
/// Reports Degraded, not Unhealthy: an unconfigured integration is a
/// deliberate, supported state (see SmtpSettings/NotConfiguredRemoteExecutionProvider
/// doc comments), not an outage — this exists purely so an operator can see
/// what's live without digging through configuration.
/// </summary>
public class IntegrationsHealthCheck(IOptions<SmtpSettings> smtpSettings, IRemoteExecutionProvider remoteExecutionProvider) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var data = new Dictionary<string, object>
        {
            ["smtp"] = string.IsNullOrWhiteSpace(smtpSettings.Value.Host) ? "not configured" : "configured",
            ["remoteExecution"] = remoteExecutionProvider is NotConfiguredRemoteExecutionProvider ? "not configured" : "configured",
        };

        var anyUnconfigured = data.Values.Contains("not configured");
        return Task.FromResult(anyUnconfigured
            ? HealthCheckResult.Degraded("One or more optional integrations are not configured.", data: data)
            : HealthCheckResult.Healthy("All known integrations are configured.", data));
    }
}
