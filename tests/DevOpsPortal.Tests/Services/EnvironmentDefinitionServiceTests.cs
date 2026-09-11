using DevOpsPortal.Application.Dtos.Environments;
using DevOpsPortal.Application.Services;
using DevOpsPortal.Tests.Common;
using Xunit;

namespace DevOpsPortal.Tests.Services;

public class EnvironmentDefinitionServiceTests
{
    private static EnvironmentDefinitionService CreateSut(out Infrastructure.Persistence.AppDbContext db)
    {
        db = TestDb.CreateInMemory();
        var audit = new AuditService(db, new FakeCurrentUserService());
        return new EnvironmentDefinitionService(db, audit);
    }

    [Fact]
    public async Task GetAllAsync_ReturnsSeededStagesInPipelineOrder()
    {
        var sut = CreateSut(out var db);
        await TestDb.SeedEnvironmentDefinitionsAsync(db);

        var result = await sut.GetAllAsync();

        Assert.Equal(["DEV", "QA", "UAT", "PRODUCTION"], result.Select(e => e.Name));
        Assert.True(result.Single(e => e.Name == "PRODUCTION").IsProductionLike);
        Assert.False(result.Single(e => e.Name == "DEV").IsProductionLike);
    }

    [Fact]
    public async Task UpdateAsync_OnlyChangesIsProductionLikeAndIsActive_NameAndSortOrderUnchanged()
    {
        var sut = CreateSut(out var db);
        await TestDb.SeedEnvironmentDefinitionsAsync(db);
        var qa = (await sut.GetAllAsync()).Single(e => e.Name == "QA");

        var updated = await sut.UpdateAsync(qa.Id, new UpdateEnvironmentDefinitionRequest(IsProductionLike: true, IsActive: false));

        Assert.Equal("QA", updated.Name);
        Assert.Equal(qa.SortOrder, updated.SortOrder);
        Assert.True(updated.IsProductionLike);
        Assert.False(updated.IsActive);
    }
}
