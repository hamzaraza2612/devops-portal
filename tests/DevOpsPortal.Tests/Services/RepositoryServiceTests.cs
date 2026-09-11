using DevOpsPortal.Application.Dtos.Repositories;
using DevOpsPortal.Application.Exceptions;
using DevOpsPortal.Application.Services;
using DevOpsPortal.Domain.Enums;
using DevOpsPortal.Tests.Common;
using Xunit;

namespace DevOpsPortal.Tests.Services;

public class RepositoryServiceTests
{
    private static RepositoryService CreateSut() => CreateSut(out _);

    private static RepositoryService CreateSut(out FakeSecretProvider secretProvider)
    {
        var db = TestDb.CreateInMemory();
        secretProvider = new FakeSecretProvider();
        return new RepositoryService(
            db, new AuditService(db, new FakeCurrentUserService()), secretProvider, new FakeGitProviderClient());
    }

    [Fact]
    public async Task CreateAsync_WithValidHttpsUrl_Succeeds()
    {
        var sut = CreateSut();

        var dto = await sut.CreateAsync(new CreateRepositoryRequest(
            "sample-repo", "https://gitlab.example.com/group/sample-repo.git", RepositoryProvider.GitLab, null, null, null, null));

        Assert.Equal("sample-repo", dto.Name);
    }

    [Fact]
    public async Task CreateAsync_WithEmbeddedCredentialsInUrl_ThrowsValidation()
    {
        var sut = CreateSut();

        await Assert.ThrowsAsync<ValidationException>(() => sut.CreateAsync(new CreateRepositoryRequest(
            "sample-repo", "https://user:secretpassword@gitlab.example.com/group/repo.git", RepositoryProvider.GitLab, null, null, null, null)));
    }

    [Theory]
    [InlineData("not-a-url")]
    [InlineData("ftp://gitlab.example.com/repo.git")]
    public async Task CreateAsync_WithInvalidUrl_ThrowsValidation(string url)
    {
        var sut = CreateSut();

        await Assert.ThrowsAsync<ValidationException>(() =>
            sut.CreateAsync(new CreateRepositoryRequest("sample-repo", url, RepositoryProvider.GitLab, null, null, null, null)));
    }

    [Fact]
    public async Task CreateAsync_WithDuplicateName_ThrowsConflict()
    {
        var sut = CreateSut();
        await sut.CreateAsync(new CreateRepositoryRequest("sample-repo", "https://gitlab.example.com/a.git", RepositoryProvider.GitLab, null, null, null, null));

        await Assert.ThrowsAsync<ConflictException>(() =>
            sut.CreateAsync(new CreateRepositoryRequest("sample-repo", "https://gitlab.example.com/b.git", RepositoryProvider.GitLab, null, null, null, null)));
    }

    [Fact]
    public async Task CreateAsync_WithValidAccessTokenEnvVarName_Succeeds()
    {
        var sut = CreateSut();

        var dto = await sut.CreateAsync(new CreateRepositoryRequest(
            "sample-repo", "https://gitlab.example.com/group/sample-repo.git", RepositoryProvider.GitLab, null, null, null, "GITLAB_TOKEN_SAMPLE"));

        Assert.Equal("GITLAB_TOKEN_SAMPLE", dto.AccessTokenEnvVarName);
    }

    [Theory]
    [InlineData("glpat-abcdef1234567890")] // looks like an actual token, not an env var name
    [InlineData("lowercase_name")]
    [InlineData("x")]
    public async Task CreateAsync_WithInvalidAccessTokenEnvVarName_ThrowsValidation(string envVarName)
    {
        var sut = CreateSut();

        await Assert.ThrowsAsync<ValidationException>(() => sut.CreateAsync(new CreateRepositoryRequest(
            "sample-repo", "https://gitlab.example.com/group/sample-repo.git", RepositoryProvider.GitLab, null, null, null, envVarName)));
    }

    [Fact]
    public async Task DeleteAsync_RemovesTheRepositoryAndItsStoredAccessToken()
    {
        // Regression test (Phase 13 final review): DeleteAsync used to remove
        // the Repository row without ever calling ISecretProvider.DeleteAsync
        // on its AccessTokenStoreKey — leaving the encrypted token orphaned in
        // the secret store forever.
        var sut = CreateSut(out var secretProvider);
        var repo = await sut.CreateAsync(new CreateRepositoryRequest(
            "sample-repo", "https://gitlab.example.com/group/sample-repo.git", RepositoryProvider.GitLab, null, null, null, null));
        await sut.SetAccessTokenAsync(repo.Id, new SetRepositoryAccessTokenRequest("glpat-super-secret-token"));
        Assert.Equal(1, secretProvider.StoredValueCount);

        await sut.DeleteAsync(repo.Id);

        await Assert.ThrowsAsync<NotFoundException>(() => sut.GetByIdAsync(repo.Id));
        Assert.Equal(0, secretProvider.StoredValueCount);
    }
}
