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

        var updated = await sut.UpdateAsync(qa.Id, new UpdateEnvironmentDefinitionRequest(IsProductionLike: true, IsActive: false, PrimaryTargetServerId: null));

        Assert.Equal("QA", updated.Name);
        Assert.Equal(qa.SortOrder, updated.SortOrder);
        Assert.True(updated.IsProductionLike);
        Assert.False(updated.IsActive);
    }

    [Fact]
    public async Task UpdateAsync_WithKnownTargetServer_SetsPrimaryTargetServer()
    {
        var sut = CreateSut(out var db);
        await TestDb.SeedEnvironmentDefinitionsAsync(db);
        var dev = (await sut.GetAllAsync()).Single(e => e.Name == "DEV");
        var server = new DevOpsPortal.Domain.Entities.TargetServer { Name = "dev-server" };
        db.TargetServers.Add(server);
        await db.SaveChangesAsync();

        var updated = await sut.UpdateAsync(dev.Id, new UpdateEnvironmentDefinitionRequest(IsProductionLike: false, IsActive: true, PrimaryTargetServerId: server.Id));

        Assert.Equal(server.Id, updated.PrimaryTargetServerId);
        Assert.Equal("dev-server", updated.PrimaryTargetServerName);
    }

    [Fact]
    public async Task UpdateAsync_WithUnknownTargetServer_ThrowsValidation()
    {
        var sut = CreateSut(out var db);
        await TestDb.SeedEnvironmentDefinitionsAsync(db);
        var dev = (await sut.GetAllAsync()).Single(e => e.Name == "DEV");

        await Assert.ThrowsAsync<DevOpsPortal.Application.Exceptions.ValidationException>(() =>
            sut.UpdateAsync(dev.Id, new UpdateEnvironmentDefinitionRequest(IsProductionLike: false, IsActive: true, PrimaryTargetServerId: Guid.NewGuid())));
    }
}
