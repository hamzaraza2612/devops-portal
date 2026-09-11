using DevOpsPortal.Application.Abstractions;
using DevOpsPortal.Domain.Entities;
using DevOpsPortal.Infrastructure.Deployments;
using DevOpsPortal.Infrastructure.Email;
using DevOpsPortal.Infrastructure.Integrations;
using DevOpsPortal.Infrastructure.Remote;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Xunit;

namespace DevOpsPortal.Tests.Deployments;

public class BackgroundWorkerHealthCheckTests
{
    [Fact]
    public async Task CheckHealthAsync_WhenWorkerIsNotRegistered_ReturnsUnhealthy()
    {
        var sut = new BackgroundWorkerHealthCheck([]);

        var result = await sut.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
    }

    [Fact]
    public async Task CheckHealthAsync_WhenAnotherHostedServiceIsCompletedButWorkerIsNotPresent_ReturnsUnhealthy()
    {
        var sut = new BackgroundWorkerHealthCheck([new FakeUnrelatedHostedService()]);

        var result = await sut.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
    }

    private sealed class FakeUnrelatedHostedService : IHostedService
    {
        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}

public class IntegrationsHealthCheckTests
{
    [Fact]
    public async Task CheckHealthAsync_WhenNothingIsConfigured_ReturnsDegraded()
    {
        var sut = new IntegrationsHealthCheck(
            Options.Create(new SmtpSettings()), new NotConfiguredRemoteExecutionProvider(NullLoggerFor<NotConfiguredRemoteExecutionProvider>()));

        var result = await sut.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Degraded, result.Status);
    }

    [Fact]
    public async Task CheckHealthAsync_WhenSmtpIsConfiguredButRemoteExecutionIsNot_StillReportsDegraded()
    {
        var sut = new IntegrationsHealthCheck(
            Options.Create(new SmtpSettings { Host = "smtp.example.com" }),
            new NotConfiguredRemoteExecutionProvider(NullLoggerFor<NotConfiguredRemoteExecutionProvider>()));

        var result = await sut.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Degraded, result.Status);
        Assert.Equal("configured", result.Data["smtp"]);
        Assert.Equal("not configured", result.Data["remoteExecution"]);
    }

    [Fact]
    public async Task CheckHealthAsync_WhenAllIntegrationsAreConfigured_ReturnsHealthy()
    {
        var sut = new IntegrationsHealthCheck(
            Options.Create(new SmtpSettings { Host = "smtp.example.com" }), new FakeConfiguredRemoteExecutionProvider());

        var result = await sut.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Healthy, result.Status);
    }

    private static Microsoft.Extensions.Logging.ILogger<T> NullLoggerFor<T>() => Microsoft.Extensions.Logging.Abstractions.NullLogger<T>.Instance;

    private sealed class FakeConfiguredRemoteExecutionProvider : IRemoteExecutionProvider
    {
        public bool IsConfigured(TargetServer targetServer) => true;

        public Task<ComposeCommandResult> RunComposeAsync(
            TargetServer targetServer, ComposeCommandRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<RemoteContainerInspectResult> InspectContainerAsync(
            TargetServer targetServer, string containerName, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<RemoteConnectionTestResult> TestConnectionAsync(TargetServer targetServer, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
