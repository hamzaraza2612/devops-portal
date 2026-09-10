using DevOpsPortal.Application.Services;
using DevOpsPortal.Tests.Common;
using Xunit;

namespace DevOpsPortal.Tests.Services;

public class EnvironmentDefinitionServiceTests
{
    [Fact]
    public async Task GetAllAsync_ReturnsSeededStagesInPipelineOrder()
    {
        var db = TestDb.CreateInMemory();
        await TestDb.SeedEnvironmentDefinitionsAsync(db);
        var sut = new EnvironmentDefinitionService(db);

        var result = await sut.GetAllAsync();

        Assert.Equal(["DEV", "QA", "UAT", "PRODUCTION"], result.Select(e => e.Name));
        Assert.True(result.Single(e => e.Name == "PRODUCTION").IsProductionLike);
        Assert.False(result.Single(e => e.Name == "DEV").IsProductionLike);
    }
}
