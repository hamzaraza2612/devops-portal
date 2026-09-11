using DevOpsPortal.Application.Abstractions;
using DevOpsPortal.Domain.Entities;
using DevOpsPortal.Domain.Enums;
using DevOpsPortal.Infrastructure.Remote;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DevOpsPortal.Tests.Remote;

public class DockerComposeContainerRuntimeProviderTests
{
    private static readonly TargetServer Server = new() { Name = "server-1", Hostname = "10.0.0.5" };

    private static DockerComposeContainerRuntimeProvider CreateSut(IRemoteExecutionProvider remoteExecutionProvider) =>
        new(remoteExecutionProvider, NullLogger<DockerComposeContainerRuntimeProvider>.Instance);

    // ------------------------------------------------------------------- status

    [Fact]
    public async Task GetStatusAsync_WhenTargetServerNotConfigured_ReturnsUnreachable_WithoutAttemptingAnything()
    {
        var remote = new FakeRemoteExecutionProvider(isConfigured: false);
        var sut = CreateSut(remote);

        var result = await sut.GetStatusAsync(Server, "/opt/apps/sample", "docker-compose.yml", null);

        Assert.False(result.IsReachable);
        Assert.NotNull(result.UnreachableReason);
        Assert.Empty(result.Containers);
        Assert.Equal(0, remote.ComposeInvocationCount);
    }

    [Fact]
    public async Task GetStatusAsync_WhenComposePsFails_ReturnsReachableWithEmptyContainers()
    {
        var remote = new FakeRemoteExecutionProvider(isConfigured: true, psSuccess: false, psOutput: string.Empty);
        var sut = CreateSut(remote);

        var result = await sut.GetStatusAsync(Server, "/opt/apps/sample", "docker-compose.yml", null);

        Assert.True(result.IsReachable);
        Assert.Empty(result.Containers);
    }

    [Fact]
    public async Task GetStatusAsync_WhenPsOutputIsAJsonArray_DiscoversEachContainer()
    {
        var psOutput = """
            [
              {"Name":"sampleapp-web-1","Service":"web","Image":"nginx:1.25-alpine","State":"running","Health":""},
              {"Name":"sampleapp-worker-1","Service":"worker","Image":"sampleapp/worker:abc1234","State":"exited","Health":""}
            ]
            """;
        var remote = new FakeRemoteExecutionProvider(isConfigured: true, psSuccess: true, psOutput: psOutput);
        var sut = CreateSut(remote);

        // InspectContainerAsync fails (default fake behavior) — the fallback from
        // the ps entry's own fields is what's actually being exercised here.
        var result = await sut.GetStatusAsync(Server, "/opt/apps/sample", "docker-compose.yml", null);

        Assert.True(result.IsReachable);
        Assert.Equal(2, result.Containers.Count);
        var web = Assert.Single(result.Containers, c => c.ServiceName == "web");
        Assert.Equal("sampleapp-web-1", web.ContainerName);
        Assert.Equal("nginx", web.Image);
        Assert.Equal("1.25-alpine", web.ImageTag);
        Assert.Equal(ContainerState.Running, web.State);

        var worker = Assert.Single(result.Containers, c => c.ServiceName == "worker");
        Assert.Equal(ContainerState.Exited, worker.State);
        Assert.Equal("sampleapp/worker", worker.Image);
        Assert.Equal("abc1234", worker.ImageTag);
    }

    [Fact]
    public async Task GetStatusAsync_WhenPsOutputIsNewlineDelimitedJson_StillParsesEachLine()
    {
        var psOutput =
            """{"Name":"sampleapp-web-1","Service":"web","Image":"nginx:alpine","State":"running","Health":""}""" + "\n" +
            """{"Name":"sampleapp-db-1","Service":"db","Image":"postgres:16","State":"paused","Health":""}""";
        var remote = new FakeRemoteExecutionProvider(isConfigured: true, psSuccess: true, psOutput: psOutput);
        var sut = CreateSut(remote);

        var result = await sut.GetStatusAsync(Server, "/opt/apps/sample", "docker-compose.yml", null);

        Assert.Equal(2, result.Containers.Count);
        Assert.Contains(result.Containers, c => c.ServiceName == "web" && c.State == ContainerState.Running);
        Assert.Contains(result.Containers, c => c.ServiceName == "db" && c.State == ContainerState.Paused);
    }

    [Fact]
    public async Task GetStatusAsync_WithMalformedPsOutput_ReturnsEmptyRatherThanThrowing()
    {
        var remote = new FakeRemoteExecutionProvider(isConfigured: true, psSuccess: true, psOutput: "not json at all { garbage");
        var sut = CreateSut(remote);

        var result = await sut.GetStatusAsync(Server, "/opt/apps/sample", "docker-compose.yml", null);

        Assert.True(result.IsReachable);
        Assert.Empty(result.Containers);
    }

    [Fact]
    public async Task GetStatusAsync_UnhealthyReportedByComposePs_MapsToUnhealthyState()
    {
        var psOutput = """[{"Name":"sampleapp-web-1","Service":"web","Image":"sampleapp:1.0","State":"running","Health":"unhealthy"}]""";
        var remote = new FakeRemoteExecutionProvider(isConfigured: true, psSuccess: true, psOutput: psOutput);
        var sut = CreateSut(remote);

        var result = await sut.GetStatusAsync(Server, "/opt/apps/sample", "docker-compose.yml", null);

        Assert.Equal(ContainerState.Unhealthy, Assert.Single(result.Containers).State);
    }

    [Fact]
    public async Task GetStatusAsync_WhenInspectSucceeds_PrefersInspectFieldsOverPsFields()
    {
        var psOutput = """[{"Name":"sampleapp-web-1","Service":"web","Image":"nginx:alpine","State":"running","Health":""}]""";
        var inspectJson = """
            [{"Name":"/sampleapp-web-1","State":{"Status":"running","StartedAt":"2025-01-01T00:00:00Z","Health":{"Status":"healthy"}},
              "RestartCount":2,"Config":{"Image":"nginx:1.27-alpine"},"NetworkSettings":{"Ports":{}}}]
            """;
        var remote = new FakeRemoteExecutionProvider(isConfigured: true, psSuccess: true, psOutput: psOutput, inspectSuccess: true, inspectJson: inspectJson);
        var sut = CreateSut(remote);

        var result = await sut.GetStatusAsync(Server, "/opt/apps/sample", "docker-compose.yml", null);

        var web = Assert.Single(result.Containers);
        Assert.Equal(2, web.RestartCount);
        Assert.Equal("1.27-alpine", web.ImageTag);
        Assert.NotNull(web.StartedAt);
    }

    // ---------------------------------------------------------------- operations

    [Fact]
    public async Task RunOperationAsync_WhenTargetServerNotConfigured_ReturnsUnreachable_WithoutAttemptingAnything()
    {
        var remote = new FakeRemoteExecutionProvider(isConfigured: false);
        var sut = CreateSut(remote);

        var result = await sut.RunOperationAsync(Server, "/opt/apps/sample", "docker-compose.yml", null, ComposeOperation.Restart);

        Assert.False(result.IsReachable);
        Assert.Equal(0, remote.ComposeInvocationCount);
    }

    [Fact]
    public async Task RunOperationAsync_WhenConfigured_DelegatesToRemoteExecutionProvider()
    {
        var remote = new FakeRemoteExecutionProvider(isConfigured: true, composeSuccess: true, composeOutput: "Restarted.");
        var sut = CreateSut(remote);

        var result = await sut.RunOperationAsync(Server, "/opt/apps/sample", "docker-compose.yml", null, ComposeOperation.Restart);

        Assert.True(result.IsReachable);
        Assert.True(result.Success);
        Assert.Equal(ComposeOperation.Restart, remote.LastComposeOperation);
    }

    private sealed class FakeRemoteExecutionProvider(
        bool isConfigured,
        bool psSuccess = false,
        string psOutput = "",
        bool inspectSuccess = false,
        string inspectJson = "",
        bool composeSuccess = false,
        string composeOutput = "") : IRemoteExecutionProvider
    {
        public int ComposeInvocationCount { get; private set; }
        public ComposeOperation? LastComposeOperation { get; private set; }

        public bool IsConfigured(TargetServer targetServer) => isConfigured;

        public Task<ComposeCommandResult> RunComposeAsync(TargetServer targetServer, ComposeCommandRequest request, CancellationToken cancellationToken = default)
        {
            ComposeInvocationCount++;
            LastComposeOperation = request.Operation;
            var success = request.Operation == ComposeOperation.Ps ? psSuccess : composeSuccess;
            var output = request.Operation == ComposeOperation.Ps ? psOutput : composeOutput;
            return Task.FromResult(new ComposeCommandResult(success, success ? 0 : 1, success ? output : string.Empty, success ? string.Empty : "failed"));
        }

        public Task<RemoteContainerInspectResult> InspectContainerAsync(TargetServer targetServer, string containerName, CancellationToken cancellationToken = default) =>
            Task.FromResult(new RemoteContainerInspectResult(inspectSuccess, inspectSuccess ? inspectJson : string.Empty, inspectSuccess ? null : "inspect failed"));

        public Task<RemoteConnectionTestResult> TestConnectionAsync(TargetServer targetServer, CancellationToken cancellationToken = default) =>
            Task.FromResult(new RemoteConnectionTestResult(isConfigured, null, null, false, null, false, null, isConfigured ? null : "not configured"));
    }
}
