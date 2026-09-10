using DevOpsPortal.Application.Dtos.Builds;
using DevOpsPortal.Application.Exceptions;
using DevOpsPortal.Application.Services;
using DevOpsPortal.Domain.Entities;
using DevOpsPortal.Domain.Enums;
using DevOpsPortal.Tests.Common;
using Xunit;

namespace DevOpsPortal.Tests.Services;

public class BuildConfigurationServiceTests
{
    private static async Task<(BuildConfigurationService Sut, ManagedApplication App)> CreateSutAsync()
    {
        var db = TestDb.CreateInMemory();
        var app = new ManagedApplication { Name = "Sample", Slug = "sample", DeploymentMode = DeploymentMode.ContainerImage };
        db.Applications.Add(app);
        await db.SaveChangesAsync();

        var sut = new BuildConfigurationService(db, new AuditService(db, new FakeCurrentUserService()));
        return (sut, app);
    }

    [Fact]
    public async Task GetAsync_WhenNotConfigured_ReturnsNull()
    {
        var (sut, app) = await CreateSutAsync();
        Assert.Null(await sut.GetAsync(app.Id));
    }

    [Fact]
    public async Task GetAsync_UnknownApplication_ThrowsNotFound()
    {
        var (sut, _) = await CreateSutAsync();
        await Assert.ThrowsAsync<NotFoundException>(() => sut.GetAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task UpsertAsync_CreatesThenUpdatesInPlace()
    {
        var (sut, app) = await CreateSutAsync();

        var created = await sut.UpsertAsync(app.Id, new UpsertBuildConfigurationRequest(
            "src/Api/Api.csproj", "Release", "src/Api/Dockerfile", "registry.example.com", "group/app", ImageTagStrategy.CommitSha));
        Assert.Equal("src/Api/Api.csproj", created.ProjectOrSolutionPath);

        var updated = await sut.UpsertAsync(app.Id, new UpsertBuildConfigurationRequest(
            "src/Api/Api.csproj", "Debug", null, null, null, ImageTagStrategy.BuildNumber));

        Assert.Equal(created.Id, updated.Id);
        Assert.Equal("Debug", updated.PublishConfiguration);
        Assert.Null(updated.DockerfilePath);
        Assert.Equal(ImageTagStrategy.BuildNumber, updated.ImageTagStrategy);
    }

    [Theory]
    [InlineData("/absolute/path")]
    [InlineData("../escape")]
    public async Task UpsertAsync_WithUnsafeProjectPath_ThrowsValidation(string path)
    {
        var (sut, app) = await CreateSutAsync();

        await Assert.ThrowsAsync<ValidationException>(() => sut.UpsertAsync(app.Id, new UpsertBuildConfigurationRequest(
            path, null, null, null, null, ImageTagStrategy.CommitSha)));
    }
}
