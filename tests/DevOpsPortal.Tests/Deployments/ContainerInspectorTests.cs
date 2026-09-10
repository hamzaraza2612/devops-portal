using DevOpsPortal.Application.Abstractions;
using DevOpsPortal.Domain.Enums;
using DevOpsPortal.Infrastructure.Deployments;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DevOpsPortal.Tests.Deployments;

public class ContainerInspectorTests
{
    private static ContainerInspector CreateSut(IComposeCommandExecutor composeExecutor) =>
        new(composeExecutor, NullLogger<ContainerInspector>.Instance);

    [Fact]
    public async Task GetStatusAsync_WhenComposePsFails_ReturnsEmptyListWithoutThrowing()
    {
        var sut = CreateSut(new FakeComposeCommandExecutor(psSuccess: false, psOutput: string.Empty));

        var result = await sut.GetStatusAsync("/tmp/does-not-matter", "docker-compose.yml", null);

        Assert.Empty(result);
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
        var sut = CreateSut(new FakeComposeCommandExecutor(psSuccess: true, psOutput: psOutput));

        // docker inspect will fail for real (no daemon in the test environment), so
        // the inspector falls back to the fields `compose ps` itself already gave —
        // this still proves discovery + fallback mapping work end to end.
        var result = await sut.GetStatusAsync("/tmp/does-not-matter", "docker-compose.yml", null);

        Assert.Equal(2, result.Count);
        var web = Assert.Single(result, c => c.ServiceName == "web");
        Assert.Equal("sampleapp-web-1", web.ContainerName);
        Assert.Equal("nginx", web.Image);
        Assert.Equal("1.25-alpine", web.ImageTag);
        Assert.Equal(ContainerState.Running, web.State);

        var worker = Assert.Single(result, c => c.ServiceName == "worker");
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
        var sut = CreateSut(new FakeComposeCommandExecutor(psSuccess: true, psOutput: psOutput));

        var result = await sut.GetStatusAsync("/tmp/does-not-matter", "docker-compose.yml", null);

        Assert.Equal(2, result.Count);
        Assert.Contains(result, c => c.ServiceName == "web" && c.State == ContainerState.Running);
        Assert.Contains(result, c => c.ServiceName == "db" && c.State == ContainerState.Paused);
    }

    [Fact]
    public async Task GetStatusAsync_WithMalformedPsOutput_ReturnsEmptyRatherThanThrowing()
    {
        var sut = CreateSut(new FakeComposeCommandExecutor(psSuccess: true, psOutput: "not json at all { garbage"));

        var result = await sut.GetStatusAsync("/tmp/does-not-matter", "docker-compose.yml", null);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetStatusAsync_UnhealthyReportedByComposePs_MapsToUnhealthyState()
    {
        var psOutput = """[{"Name":"sampleapp-web-1","Service":"web","Image":"sampleapp:1.0","State":"running","Health":"unhealthy"}]""";
        var sut = CreateSut(new FakeComposeCommandExecutor(psSuccess: true, psOutput: psOutput));

        var result = await sut.GetStatusAsync("/tmp/does-not-matter", "docker-compose.yml", null);

        Assert.Equal(ContainerState.Unhealthy, Assert.Single(result).State);
    }

    private sealed class FakeComposeCommandExecutor(bool psSuccess, string psOutput) : IComposeCommandExecutor
    {
        public Task<ComposeCommandResult> RunAsync(ComposeCommandRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ComposeCommandResult(psSuccess, psSuccess ? 0 : 1, psOutput, psSuccess ? string.Empty : "docker unreachable"));
    }
}
