using DevOpsPortal.Application.Dtos.Repositories;
using DevOpsPortal.Application.Exceptions;
using DevOpsPortal.Application.Services;
using DevOpsPortal.Domain.Enums;
using DevOpsPortal.Tests.Common;
using Xunit;

namespace DevOpsPortal.Tests.Services;

public class RepositoryServiceTests
{
    private static RepositoryService CreateSut()
    {
        var db = TestDb.CreateInMemory();
        return new RepositoryService(db, new AuditService(db, new FakeCurrentUserService()));
    }

    [Fact]
    public async Task CreateAsync_WithValidHttpsUrl_Succeeds()
    {
        var sut = CreateSut();

        var dto = await sut.CreateAsync(new CreateRepositoryRequest(
            "sample-repo", "https://gitlab.example.com/group/sample-repo.git", RepositoryProvider.GitLab, null));

        Assert.Equal("sample-repo", dto.Name);
    }

    [Fact]
    public async Task CreateAsync_WithEmbeddedCredentialsInUrl_ThrowsValidation()
    {
        var sut = CreateSut();

        await Assert.ThrowsAsync<ValidationException>(() => sut.CreateAsync(new CreateRepositoryRequest(
            "sample-repo", "https://user:secretpassword@gitlab.example.com/group/repo.git", RepositoryProvider.GitLab, null)));
    }

    [Theory]
    [InlineData("not-a-url")]
    [InlineData("ftp://gitlab.example.com/repo.git")]
    public async Task CreateAsync_WithInvalidUrl_ThrowsValidation(string url)
    {
        var sut = CreateSut();

        await Assert.ThrowsAsync<ValidationException>(() =>
            sut.CreateAsync(new CreateRepositoryRequest("sample-repo", url, RepositoryProvider.GitLab, null)));
    }

    [Fact]
    public async Task CreateAsync_WithDuplicateName_ThrowsConflict()
    {
        var sut = CreateSut();
        await sut.CreateAsync(new CreateRepositoryRequest("sample-repo", "https://gitlab.example.com/a.git", RepositoryProvider.GitLab, null));

        await Assert.ThrowsAsync<ConflictException>(() =>
            sut.CreateAsync(new CreateRepositoryRequest("sample-repo", "https://gitlab.example.com/b.git", RepositoryProvider.GitLab, null)));
    }
}
