using DevOpsPortal.Application.Abstractions;
using DevOpsPortal.Domain.Entities;
using DevOpsPortal.Infrastructure.Remote;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DevOpsPortal.Tests.Remote;

/// <summary>Verifies the honesty contract this class exists for: it must never
/// report success and must never attempt to reach anything, for every target
/// server, unconditionally — this is what stands between the portal and
/// silently monitoring/controlling the wrong machine.</summary>
public class NotConfiguredRemoteExecutionProviderTests
{
    private static NotConfiguredRemoteExecutionProvider CreateSut() => new(NullLogger<NotConfiguredRemoteExecutionProvider>.Instance);

    [Fact]
    public void IsConfigured_IsAlwaysFalse()
    {
        var sut = CreateSut();
        Assert.False(sut.IsConfigured(new TargetServer { Name = "any-server", Hostname = "10.0.0.1" }));
    }

    [Fact]
    public async Task RunComposeAsync_AlwaysFails_WithATargetServerSpecificMessage_AndNeverThrows()
    {
        var sut = CreateSut();
        var targetServer = new TargetServer { Name = "prod-server-1", Hostname = "10.0.0.9" };

        var result = await sut.RunComposeAsync(
            targetServer, new ComposeCommandRequest("/opt/apps/sample", "docker-compose.yml", null, ComposeOperation.Restart));

        Assert.False(result.Success);
        Assert.Contains("prod-server-1", result.StandardError);
        Assert.Contains("No remote execution mechanism is configured", result.StandardError);
    }

    [Fact]
    public async Task InspectContainerAsync_AlwaysFails_AndNeverThrows()
    {
        var sut = CreateSut();
        var targetServer = new TargetServer { Name = "prod-server-1", Hostname = "10.0.0.9" };

        var result = await sut.InspectContainerAsync(targetServer, "sample-web-1");

        Assert.False(result.Success);
        Assert.Empty(result.RawJson);
        Assert.Contains("prod-server-1", result.Error);
    }
}
