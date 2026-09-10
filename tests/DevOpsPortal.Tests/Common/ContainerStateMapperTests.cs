using DevOpsPortal.Application.Common;
using DevOpsPortal.Domain.Enums;
using Xunit;

namespace DevOpsPortal.Tests.Common;

public class ContainerStateMapperTests
{
    [Theory]
    [InlineData("running", ContainerState.Running)]
    [InlineData("Running", ContainerState.Running)]
    [InlineData("exited", ContainerState.Exited)]
    [InlineData("restarting", ContainerState.Restarting)]
    [InlineData("paused", ContainerState.Paused)]
    [InlineData("created", ContainerState.Created)]
    [InlineData("dead", ContainerState.Unknown)]
    [InlineData("removing", ContainerState.Unknown)]
    [InlineData("something-unexpected", ContainerState.Unknown)]
    [InlineData(null, ContainerState.Unknown)]
    public void Map_WithoutHealthStatus_MapsDockerStateDirectly(string? dockerState, ContainerState expected)
    {
        Assert.Equal(expected, ContainerStateMapper.Map(dockerState, dockerHealthStatus: null));
    }

    [Fact]
    public void Map_WithUnhealthyDockerHealth_OverridesRunningState()
    {
        Assert.Equal(ContainerState.Unhealthy, ContainerStateMapper.Map("running", "unhealthy"));
    }

    [Fact]
    public void Map_WithUnhealthyDockerHealth_IsCaseInsensitive()
    {
        Assert.Equal(ContainerState.Unhealthy, ContainerStateMapper.Map("running", "UNHEALTHY"));
    }

    [Theory]
    [InlineData("healthy")]
    [InlineData("starting")]
    [InlineData(null)]
    public void Map_WithNonUnhealthyDockerHealth_UsesDockerStateAsNormal(string? health)
    {
        Assert.Equal(ContainerState.Running, ContainerStateMapper.Map("running", health));
    }

    [Fact]
    public void Map_UnhealthyOverridesEvenAnExitedContainer()
    {
        // A container can report a stale "unhealthy" health status right up until it
        // fully exits; the mapper still prioritizes the health signal — this is a
        // documented tradeoff, not an oversight.
        Assert.Equal(ContainerState.Unhealthy, ContainerStateMapper.Map("exited", "unhealthy"));
    }
}
