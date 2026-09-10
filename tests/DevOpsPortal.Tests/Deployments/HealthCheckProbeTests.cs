using System.Net;
using System.Net.Sockets;
using System.Text;
using DevOpsPortal.Domain.Enums;
using DevOpsPortal.Infrastructure.Deployments;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DevOpsPortal.Tests.Deployments;

/// <summary>Exercises the real implementation against real loopback listeners —
/// no mocking of the probe itself — so these genuinely prove the HTTP/TCP
/// checks work, not just that a fake returns what we told it to.</summary>
public class HealthCheckProbeTests
{
    private static HealthCheckProbe CreateSut() => new(new HttpClient(), NullLogger<HealthCheckProbe>.Instance);

    [Fact]
    public async Task ProbeAsync_None_AlwaysPasses()
    {
        var result = await CreateSut().ProbeAsync(HealthCheckType.None, null, 5);
        Assert.True(result.Passed);
    }

    [Fact]
    public async Task ProbeAsync_Http_WithSuccessResponse_Passes()
    {
        using var server = await FakeHttpServer.StartAsync(200);
        var result = await CreateSut().ProbeAsync(HealthCheckType.Http, $"http://127.0.0.1:{server.Port}/health", 5);
        Assert.True(result.Passed);
    }

    [Fact]
    public async Task ProbeAsync_Http_WithErrorResponse_Fails()
    {
        using var server = await FakeHttpServer.StartAsync(500);
        var result = await CreateSut().ProbeAsync(HealthCheckType.Http, $"http://127.0.0.1:{server.Port}/health", 5);
        Assert.False(result.Passed);
    }

    [Fact]
    public async Task ProbeAsync_Http_WithInvalidUrl_Fails()
    {
        var result = await CreateSut().ProbeAsync(HealthCheckType.Http, "not-a-url", 5);
        Assert.False(result.Passed);
    }

    [Fact]
    public async Task ProbeAsync_Http_WhenNothingListening_FailsWithinTimeout()
    {
        // Port 1 is reserved/unlikely to have anything listening.
        var result = await CreateSut().ProbeAsync(HealthCheckType.Http, "http://127.0.0.1:1/health", 2);
        Assert.False(result.Passed);
    }

    [Fact]
    public async Task ProbeAsync_TcpPort_WhenListening_Passes()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var acceptTask = listener.AcceptTcpClientAsync();

        var result = await CreateSut().ProbeAsync(HealthCheckType.TcpPort, $"127.0.0.1:{port}", 5);

        Assert.True(result.Passed);
        (await acceptTask).Dispose();
        listener.Stop();
    }

    [Fact]
    public async Task ProbeAsync_TcpPort_WhenNothingListening_Fails()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop(); // free the port so nothing is listening on it

        var result = await CreateSut().ProbeAsync(HealthCheckType.TcpPort, $"127.0.0.1:{port}", 2);
        Assert.False(result.Passed);
    }

    [Fact]
    public async Task ProbeAsync_TcpPort_WithMalformedEndpoint_Fails()
    {
        var result = await CreateSut().ProbeAsync(HealthCheckType.TcpPort, "not-host-colon-port", 2);
        Assert.False(result.Passed);
    }

    /// <summary>Hand-rolled minimal HTTP/1.1 server — avoids any dependency on
    /// Kestrel/HttpListener just to test an outbound HTTP GET.</summary>
    private sealed class FakeHttpServer : IDisposable
    {
        private readonly TcpListener _listener;
        private readonly CancellationTokenSource _cts = new();
        private readonly Task _acceptLoop;

        public int Port { get; }

        private FakeHttpServer(TcpListener listener, int port, int statusCode)
        {
            _listener = listener;
            Port = port;
            _acceptLoop = AcceptLoopAsync(statusCode, _cts.Token);
        }

        public static Task<FakeHttpServer> StartAsync(int statusCode)
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            return Task.FromResult(new FakeHttpServer(listener, port, statusCode));
        }

        private async Task AcceptLoopAsync(int statusCode, CancellationToken cancellationToken)
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    using var client = await _listener.AcceptTcpClientAsync(cancellationToken);
                    using var stream = client.GetStream();
                    var buffer = new byte[1024];
                    await stream.ReadAsync(buffer, cancellationToken); // drain the request
                    var response = $"HTTP/1.1 {statusCode} Status\r\nContent-Length: 0\r\nConnection: close\r\n\r\n";
                    var bytes = Encoding.ASCII.GetBytes(response);
                    await stream.WriteAsync(bytes, cancellationToken);
                }
            }
            catch (OperationCanceledException) { }
            catch (ObjectDisposedException) { }
        }

        public void Dispose()
        {
            _cts.Cancel();
            _listener.Stop();
        }
    }
}
