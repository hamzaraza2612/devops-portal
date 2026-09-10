using System.Net.Sockets;
using DevOpsPortal.Application.Abstractions;
using DevOpsPortal.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace DevOpsPortal.Infrastructure.Deployments;

public class HealthCheckProbe(HttpClient httpClient, ILogger<HealthCheckProbe> logger) : IHealthCheckProbe
{
    public Task<HealthCheckResult> ProbeAsync(
        HealthCheckType type, string? endpoint, int timeoutSeconds, CancellationToken cancellationToken = default) => type switch
    {
        HealthCheckType.None => Task.FromResult(new HealthCheckResult(true, "No health check configured.")),
        HealthCheckType.Http => ProbeHttpAsync(endpoint, timeoutSeconds, cancellationToken),
        HealthCheckType.TcpPort => ProbeTcpAsync(endpoint, timeoutSeconds, cancellationToken),
        _ => Task.FromResult(new HealthCheckResult(false, $"Unsupported health check type '{type}'.")),
    };

    private async Task<HealthCheckResult> ProbeHttpAsync(string? endpoint, int timeoutSeconds, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(endpoint) || !Uri.TryCreate(endpoint, UriKind.Absolute, out var uri))
            return new HealthCheckResult(false, "HealthCheckEndpoint is not a valid absolute URL.");

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, timeoutSeconds)));

        try
        {
            using var response = await httpClient.GetAsync(uri, cts.Token);
            return new HealthCheckResult(response.IsSuccessStatusCode, $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            logger.LogWarning(ex, "HTTP health check failed for {Endpoint}", uri);
            return new HealthCheckResult(false, $"Request failed: {ex.Message}");
        }
    }

    private static async Task<HealthCheckResult> ProbeTcpAsync(string? endpoint, int timeoutSeconds, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
            return new HealthCheckResult(false, "HealthCheckEndpoint is required for a TCP check.");

        var parts = endpoint.Split(':', 2);
        if (parts.Length != 2 || !int.TryParse(parts[1], out var port) || port is <= 0 or > 65535)
            return new HealthCheckResult(false, "HealthCheckEndpoint must be in 'host:port' form for a TCP check.");

        using var client = new TcpClient();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, timeoutSeconds)));

        try
        {
            await client.ConnectAsync(parts[0], port, cts.Token);
            return new HealthCheckResult(true, $"TCP connect to {endpoint} succeeded.");
        }
        catch (Exception ex) when (ex is SocketException or OperationCanceledException)
        {
            return new HealthCheckResult(false, $"TCP connect to {endpoint} failed: {ex.Message}");
        }
    }
}
