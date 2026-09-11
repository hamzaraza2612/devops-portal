using DevOpsPortal.Application.Dtos.Builds;
using DevOpsPortal.Application.Exceptions;
using DevOpsPortal.Application.Services;
using DevOpsPortal.Domain.Enums;
using DevOpsPortal.Tests.Common;
using Xunit;

namespace DevOpsPortal.Tests.Services;

public class BuildServerServiceTests
{
    private static BuildServerService CreateSut()
    {
        var db = TestDb.CreateInMemory();
        var currentTenant = new FakeCurrentTenantService();
        return new BuildServerService(db, new AuditService(db, new FakeCurrentUserService(), currentTenant), currentTenant);
    }

    [Fact]
    public async Task CreateAsync_NeverReturnsOrStoresRawSecret_OnlyTheEnvVarName()
    {
        var sut = CreateSut();

        var created = await sut.CreateAsync(new CreateBuildServerRequest(
            "jenkins-main", "Main Jenkins", BuildProviderType.Jenkins, "https://jenkins.example.com", "ci-bot", "JENKINS_TOKEN_MAIN"));

        Assert.Equal("JENKINS_TOKEN_MAIN", created.ApiTokenEnvVarName);
        Assert.Equal("ci-bot", created.Username);
    }

    [Fact]
    public async Task CreateAsync_DuplicateName_ThrowsConflict()
    {
        var sut = CreateSut();
        await sut.CreateAsync(new CreateBuildServerRequest("jenkins-main", null, BuildProviderType.Jenkins, "https://jenkins.example.com", null, null));

        await Assert.ThrowsAsync<ConflictException>(() =>
            sut.CreateAsync(new CreateBuildServerRequest("jenkins-main", null, BuildProviderType.Jenkins, "https://other.example.com", null, null)));
    }

    [Theory]
    [InlineData("not-a-url")]
    [InlineData("ftp://jenkins.example.com")]
    public async Task CreateAsync_InvalidBaseUrl_ThrowsValidation(string baseUrl)
    {
        var sut = CreateSut();
        await Assert.ThrowsAsync<ValidationException>(() =>
            sut.CreateAsync(new CreateBuildServerRequest("jenkins-main", null, BuildProviderType.Jenkins, baseUrl, null, null)));
    }

    [Fact]
    public async Task CreateAsync_WithMalformedEnvVarName_ThrowsValidation()
    {
        var sut = CreateSut();
        await Assert.ThrowsAsync<ValidationException>(() =>
            sut.CreateAsync(new CreateBuildServerRequest(
                "jenkins-main", null, BuildProviderType.Jenkins, "https://jenkins.example.com", "ci-bot", "not valid!")));
    }

    [Fact]
    public async Task UpdateAsync_UnknownId_ThrowsNotFound()
    {
        var sut = CreateSut();
        await Assert.ThrowsAsync<NotFoundException>(() =>
            sut.UpdateAsync(Guid.NewGuid(), new UpdateBuildServerRequest("x", null, "https://jenkins.example.com", null, null, true)));
    }

    [Fact]
    public async Task UpdateAsync_UpdatesFieldsInPlace()
    {
        var sut = CreateSut();
        var created = await sut.CreateAsync(new CreateBuildServerRequest(
            "jenkins-main", null, BuildProviderType.Jenkins, "https://jenkins.example.com", "ci-bot", "JENKINS_TOKEN_MAIN"));

        var updated = await sut.UpdateAsync(created.Id, new UpdateBuildServerRequest(
            "jenkins-main", "Updated description", "https://jenkins2.example.com", "new-bot", "JENKINS_TOKEN_NEW", false));

        Assert.Equal("https://jenkins2.example.com", updated.BaseUrl);
        Assert.Equal("new-bot", updated.Username);
        Assert.False(updated.IsActive);
    }
}
