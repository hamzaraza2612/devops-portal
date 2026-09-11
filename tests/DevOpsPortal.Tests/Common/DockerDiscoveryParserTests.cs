using DevOpsPortal.Application.Common;
using DevOpsPortal.Domain.Enums;
using Xunit;

namespace DevOpsPortal.Tests.Common;

/// <summary>
/// Pure parsing tests for the Environment Infrastructure Dashboard's
/// whole-server discovery (Phase 13c) — no SSH, no Docker daemon, exactly the
/// same "pure, testable without a daemon" principle as ContainerStateMapper.
/// </summary>
public class DockerDiscoveryParserTests
{
    private const string OneContainerInspectJson = """
    [
      {
        "Id": "abc123def456abc123def456abc123def456abc123def456abc123def45678",
        "Name": "/dmsapi-web-1",
        "Created": "2026-09-01T10:00:00.000000000Z",
        "State": { "Status": "running", "Health": { "Status": "healthy" }, "StartedAt": "2026-09-11T08:00:00.000000000Z" },
        "RestartCount": 2,
        "Config": { "Image": "registry.example.com/dmsapi:1.2.3" },
        "NetworkSettings": { "Ports": { "80/tcp": [ { "HostIp": "0.0.0.0", "HostPort": "8080" } ], "443/tcp": null } }
      }
    ]
    """;

    [Fact]
    public void ParseInspectArray_WithOneContainer_ExtractsEveryField()
    {
        var result = DockerDiscoveryParser.ParseInspectArray(OneContainerInspectJson);

        var container = Assert.Single(result);
        Assert.Equal("abc123def456abc123def456abc123def456abc123def456abc123def45678", container.ContainerId);
        Assert.Equal("dmsapi-web-1", container.Name);
        Assert.Equal("registry.example.com/dmsapi", container.Image);
        Assert.Equal("1.2.3", container.ImageTag);
        Assert.Equal(ContainerState.Running, container.State);
        Assert.Equal("healthy", container.DockerHealthStatus);
        Assert.Equal(2, container.RestartCount);
        Assert.NotNull(container.CreatedAt);
        Assert.NotNull(container.StartedAt);
        Assert.Contains("8080->80/tcp", container.Ports);
        Assert.Contains("443/tcp", container.Ports);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("")]
    [InlineData("   ")]
    public void ParseInspectArray_WithZeroContainers_ReturnsEmptyListNotAnError(string rawJson) =>
        Assert.Empty(DockerDiscoveryParser.ParseInspectArray(rawJson));

    [Theory]
    [InlineData("not json at all")]
    [InlineData("{\"not\": \"an array\"}")]
    [InlineData("[{\"Id\": \"abc\"}]")] // missing Name — skipped, not thrown
    public void ParseInspectArray_WithMalformedInput_NeverThrows(string rawJson)
    {
        var result = DockerDiscoveryParser.ParseInspectArray(rawJson);
        Assert.NotNull(result);
    }

    [Fact]
    public void ParseInspectArray_UnhealthyContainer_MapsToUnhealthyStateEvenThoughRunning()
    {
        const string json = """
        [{"Id":"abc123","Name":"/svc","Created":"2026-01-01T00:00:00Z","State":{"Status":"running","Health":{"Status":"unhealthy"}},"RestartCount":0,"Config":{"Image":"svc:latest"}}]
        """;
        var result = DockerDiscoveryParser.ParseInspectArray(json);
        Assert.Equal(ContainerState.Unhealthy, Assert.Single(result).State);
    }

    [Fact]
    public void ParseStatsLines_WithMultipleContainers_KeysByName()
    {
        var raw = """
        {"Name":"dmsapi-web-1","CPUPerc":"2.40%","MemUsage":"180MiB / 512MiB","MemPerc":"35.15%","NetIO":"1.2kB / 0B","BlockIO":"0B / 0B","PIDs":"5"}
        {"Name":"auth-service-1","CPUPerc":"1.10%","MemUsage":"120MiB / 512MiB","MemPerc":"23.4%","NetIO":"500B / 0B","BlockIO":"0B / 0B","PIDs":"3"}
        """;

        var result = DockerDiscoveryParser.ParseStatsLines(raw);

        Assert.Equal(2, result.Count);
        Assert.Equal(2.40, result["dmsapi-web-1"].CpuPercent);
        Assert.Equal("180MiB", result["dmsapi-web-1"].MemoryUsage);
        Assert.Equal("512MiB", result["dmsapi-web-1"].MemoryLimit);
        Assert.Equal(5, result["dmsapi-web-1"].PidCount);
        Assert.Equal(1.10, result["auth-service-1"].CpuPercent);
    }

    [Fact]
    public void ParseStatsLines_WithOneMalformedLine_StillParsesTheOthers()
    {
        var raw = "not json\n{\"Name\":\"svc-1\",\"CPUPerc\":\"1%\"}";
        var result = DockerDiscoveryParser.ParseStatsLines(raw);
        Assert.Single(result);
        Assert.True(result.ContainsKey("svc-1"));
    }

    [Fact]
    public void ParseStatsLines_Empty_ReturnsEmptyDictionary() =>
        Assert.Empty(DockerDiscoveryParser.ParseStatsLines(""));

    [Fact]
    public void ParseLoadAvg_ValidProcLoadavg_ExtractsFirstThreeFields()
    {
        var (l1, l5, l15) = DockerDiscoveryParser.ParseLoadAvg("0.66 0.54 0.28 2/456 12345");
        Assert.Equal(0.66, l1);
        Assert.Equal(0.54, l5);
        Assert.Equal(0.28, l15);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("garbage")]
    public void ParseLoadAvg_InvalidInput_ReturnsAllNullsNeverThrows(string? raw)
    {
        var (l1, l5, l15) = DockerDiscoveryParser.ParseLoadAvg(raw);
        Assert.Null(l1);
        Assert.Null(l5);
        Assert.Null(l15);
    }

    [Fact]
    public void ParseFreeBytes_ModernSevenColumnFormat_ExtractsTotalUsedAvailable()
    {
        const string raw = """
                      total        used        free      shared  buff/cache   available
        Mem:    16659575808  2762333184 12622626816   521138176  1616015360 13560396800
        Swap:             0           0           0
        """;

        var (total, used, available) = DockerDiscoveryParser.ParseFreeBytes(raw);
        Assert.Equal(16659575808, total);
        Assert.Equal(2762333184, used);
        Assert.Equal(13560396800, available);
    }

    [Fact]
    public void ParseFreeBytes_MissingMemLine_ReturnsAllNulls()
    {
        var (total, used, available) = DockerDiscoveryParser.ParseFreeBytes("Swap: 0 0 0");
        Assert.Null(total);
        Assert.Null(used);
        Assert.Null(available);
    }

    [Fact]
    public void ParseDfKb_ValidPosixOutput_ExtractsBytesAndPercent()
    {
        const string raw = """
        Filesystem     1024-blocks      Used Available Capacity Mounted on
        /dev/vda        264241408  11534336  39876608      23% /
        """;

        var (total, used, available, percent) = DockerDiscoveryParser.ParseDfKb(raw);
        Assert.Equal(264241408L * 1024, total);
        Assert.Equal(11534336L * 1024, used);
        Assert.Equal(39876608L * 1024, available);
        Assert.Equal(23, percent);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("only one line")]
    public void ParseDfKb_InvalidInput_ReturnsAllNullsNeverThrows(string? raw)
    {
        var (total, used, available, percent) = DockerDiscoveryParser.ParseDfKb(raw);
        Assert.Null(total);
        Assert.Null(used);
        Assert.Null(available);
        Assert.Null(percent);
    }
}
