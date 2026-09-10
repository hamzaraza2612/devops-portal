using DevOpsPortal.Domain.Enums;

namespace DevOpsPortal.Application.Abstractions;

public record HealthCheckResult(bool Passed, string Detail);

/// <summary>Real HTTP/TCP probing per master requirements §13 — verifies the
/// configured endpoint, never just assumes success. HealthCheckType.None
/// always passes trivially (no check configured = nothing to verify).</summary>
public interface IHealthCheckProbe
{
    Task<HealthCheckResult> ProbeAsync(
        HealthCheckType type, string? endpoint, int timeoutSeconds, CancellationToken cancellationToken = default);
}
