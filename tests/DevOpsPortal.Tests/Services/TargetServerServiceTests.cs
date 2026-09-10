using DevOpsPortal.Application.Dtos.TargetServers;
using DevOpsPortal.Application.Exceptions;
using DevOpsPortal.Application.Services;
using DevOpsPortal.Tests.Common;
using Xunit;

namespace DevOpsPortal.Tests.Services;

public class TargetServerServiceTests
{
    private static TargetServerService CreateSut()
    {
        var db = TestDb.CreateInMemory();
        return new TargetServerService(db, new AuditService(db, new FakeCurrentUserService()));
    }

    [Fact]
    public async Task CreateAsync_WithDuplicateName_ThrowsConflict()
    {
        var sut = CreateSut();
        await sut.CreateAsync(new CreateTargetServerRequest("server-1", null, null));

        await Assert.ThrowsAsync<ConflictException>(() =>
            sut.CreateAsync(new CreateTargetServerRequest("server-1", null, null)));
    }

    [Fact]
    public async Task AddAllowedRootAsync_WithValidAbsolutePath_Succeeds()
    {
        var sut = CreateSut();
        var server = await sut.CreateAsync(new CreateTargetServerRequest("server-1", null, null));

        var root = await sut.AddAllowedRootAsync(server.Id, new CreateAllowedDeploymentRootRequest("/mnt/data/apps", "primary root"));

        Assert.Equal("/mnt/data/apps", root.RootPath);
        var refreshed = await sut.GetByIdAsync(server.Id);
        Assert.Single(refreshed.AllowedDeploymentRoots);
    }

    [Theory]
    [InlineData("relative/path")]
    [InlineData("/mnt/data/../etc")]
    [InlineData("")]
    public async Task AddAllowedRootAsync_WithUnsafePath_ThrowsValidation(string path)
    {
        var sut = CreateSut();
        var server = await sut.CreateAsync(new CreateTargetServerRequest("server-1", null, null));

        await Assert.ThrowsAsync<ValidationException>(() =>
            sut.AddAllowedRootAsync(server.Id, new CreateAllowedDeploymentRootRequest(path, null)));
    }

    [Fact]
    public async Task AddAllowedRootAsync_WithDuplicateRootOnSameServer_ThrowsConflict()
    {
        var sut = CreateSut();
        var server = await sut.CreateAsync(new CreateTargetServerRequest("server-1", null, null));
        await sut.AddAllowedRootAsync(server.Id, new CreateAllowedDeploymentRootRequest("/mnt/data/apps", null));

        await Assert.ThrowsAsync<ConflictException>(() =>
            sut.AddAllowedRootAsync(server.Id, new CreateAllowedDeploymentRootRequest("/mnt/data/apps/", null)));
    }

    [Fact]
    public async Task AddAllowedRootAsync_WithUnknownServer_ThrowsNotFound()
    {
        var sut = CreateSut();

        await Assert.ThrowsAsync<NotFoundException>(() =>
            sut.AddAllowedRootAsync(Guid.NewGuid(), new CreateAllowedDeploymentRootRequest("/mnt/data/apps", null)));
    }
}
