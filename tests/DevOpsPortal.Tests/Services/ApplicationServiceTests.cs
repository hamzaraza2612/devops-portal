using DevOpsPortal.Application.Dtos.Applications;
using DevOpsPortal.Application.Exceptions;
using DevOpsPortal.Application.Services;
using DevOpsPortal.Domain.Enums;
using DevOpsPortal.Tests.Common;
using Xunit;

namespace DevOpsPortal.Tests.Services;

public class ApplicationServiceTests
{
    private static ApplicationService CreateSut(out Infrastructure.Persistence.AppDbContext db)
    {
        db = TestDb.CreateInMemory();
        var currentTenant = new FakeCurrentTenantService();
        var audit = new AuditService(db, new FakeCurrentUserService(), currentTenant);
        return new ApplicationService(db, audit, currentTenant);
    }

    [Fact]
    public async Task CreateAsync_WithValidRequest_Succeeds()
    {
        var sut = CreateSut(out var db);

        var dto = await sut.CreateAsync(new CreateApplicationRequest(
            "Sample API", "sample-api", "A sample app", DeploymentMode.LegacyFilesystem, null, null));

        Assert.Equal("sample-api", dto.Slug);
        Assert.Equal(DeploymentMode.LegacyFilesystem, dto.DeploymentMode);
        Assert.Single(db.Applications);
    }

    [Theory]
    [InlineData("Sample_API")]
    [InlineData("Sample API")]
    [InlineData("-sample-api")]
    [InlineData("sample-api-")]
    [InlineData("")]
    public async Task CreateAsync_WithInvalidSlug_ThrowsValidation(string slug)
    {
        var sut = CreateSut(out _);

        await Assert.ThrowsAsync<ValidationException>(() =>
            sut.CreateAsync(new CreateApplicationRequest("Sample", slug, null, DeploymentMode.LegacyFilesystem, null, null)));
    }

    [Fact]
    public async Task CreateAsync_WithDuplicateSlug_ThrowsConflict()
    {
        var sut = CreateSut(out _);
        await sut.CreateAsync(new CreateApplicationRequest("Sample", "sample-api", null, DeploymentMode.LegacyFilesystem, null, null));

        await Assert.ThrowsAsync<ConflictException>(() =>
            sut.CreateAsync(new CreateApplicationRequest("Sample Two", "sample-api", null, DeploymentMode.LegacyFilesystem, null, null)));
    }

    [Fact]
    public async Task CreateAsync_WithUnknownRepositoryId_ThrowsValidation()
    {
        var sut = CreateSut(out _);

        await Assert.ThrowsAsync<ValidationException>(() =>
            sut.CreateAsync(new CreateApplicationRequest("Sample", "sample-api", null, DeploymentMode.LegacyFilesystem, Guid.NewGuid(), null)));
    }

    [Theory]
    [InlineData("/absolute/path")]
    [InlineData("../escape")]
    [InlineData("nested/../../escape")]
    public async Task CreateAsync_WithUnsafeSourcePath_ThrowsValidation(string sourcePath)
    {
        var sut = CreateSut(out _);

        await Assert.ThrowsAsync<ValidationException>(() =>
            sut.CreateAsync(new CreateApplicationRequest("Sample", "sample-api", null, DeploymentMode.LegacyFilesystem, null, sourcePath)));
    }

    [Fact]
    public async Task UpdateAsync_ChangesFieldsAndTimestamps()
    {
        var sut = CreateSut(out _);
        var created = await sut.CreateAsync(new CreateApplicationRequest("Sample", "sample-api", null, DeploymentMode.LegacyFilesystem, null, null));

        var updated = await sut.UpdateAsync(created.Id, new UpdateApplicationRequest(
            "Sample Renamed", "Updated description", DeploymentMode.ContainerImage, null, "src/backend", false));

        Assert.Equal("Sample Renamed", updated.Name);
        Assert.Equal(DeploymentMode.ContainerImage, updated.DeploymentMode);
        Assert.Equal("src/backend", updated.SourcePath);
        Assert.False(updated.IsActive);
        Assert.NotNull(updated.UpdatedAt);
        Assert.Equal("sample-api", updated.Slug); // slug is immutable via update
    }
}
