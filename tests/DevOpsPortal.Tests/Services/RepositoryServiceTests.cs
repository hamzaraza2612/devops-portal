using DevOpsPortal.Application.Abstractions;
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

    private static RepositoryService CreateSut(out FakeSecretProvider secretProvider, IGitProviderClient? gitProviderClient = null) =>
        CreateSut(out secretProvider, out _, gitProviderClient);

    private static RepositoryService CreateSut(
        out FakeSecretProvider secretProvider, out DevOpsPortal.Infrastructure.Persistence.AppDbContext db, IGitProviderClient? gitProviderClient = null)
    {
        db = TestDb.CreateInMemory();
        secretProvider = new FakeSecretProvider();
        return new RepositoryService(
            db, new AuditService(db, new FakeCurrentUserService()), secretProvider, gitProviderClient ?? new FakeGitProviderClient());
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

    [Fact]
    public async Task DiscoverApplicationsAsync_ReturnsFoldersFromGitLabAndMarksExistingApplicationsLinked()
    {
        var folders = new List<DevOpsPortal.Application.Abstractions.GitRepositoryFolder>
        {
            new("dmsapi", true),
            new("hrms", true),
            new("docs", false), // no compose file — not a deployable candidate, but still shown
        };
        var gitClient = new FakeGitProviderClient(foldersResult: GitProviderResult<IReadOnlyList<DevOpsPortal.Application.Abstractions.GitRepositoryFolder>>.Ok(folders));
        var sut = CreateSut(out _, out var db, gitClient);
        var repo = await sut.CreateAsync(new CreateRepositoryRequest(
            "monorepo", "https://gitlab.example.com/group/monorepo.git", RepositoryProvider.GitLab, null, "main", null, null));

        db.Applications.Add(new DevOpsPortal.Domain.Entities.ManagedApplication
        {
            Name = "DmsApi", Slug = "dmsapi", RepositoryId = repo.Id, SourcePath = "dmsapi",
        });
        await db.SaveChangesAsync();

        var result = await sut.DiscoverApplicationsAsync(repo.Id, null);

        Assert.True(result.Success);
        Assert.Equal("main", result.Branch);
        Assert.Equal(3, result.Folders.Count);

        var dmsapi = result.Folders.Single(f => f.Path == "dmsapi");
        Assert.True(dmsapi.HasComposeFile);
        Assert.Equal("DmsApi", dmsapi.ExistingApplicationName);

        var hrms = result.Folders.Single(f => f.Path == "hrms");
        Assert.True(hrms.HasComposeFile);
        Assert.Null(hrms.ExistingApplicationName);

        var docs = result.Folders.Single(f => f.Path == "docs");
        Assert.False(docs.HasComposeFile);
        Assert.Null(docs.ExistingApplicationName);
    }

    [Fact]
    public async Task DiscoverApplicationsAsync_WhenGitLabScanFails_ReturnsFailureWithoutThrowing()
    {
        var gitClient = new FakeGitProviderClient(foldersResult: GitProviderResult<IReadOnlyList<DevOpsPortal.Application.Abstractions.GitRepositoryFolder>>.Fail("GitLab returned 401."));
        var sut = CreateSut(out _, gitClient);
        var repo = await sut.CreateAsync(new CreateRepositoryRequest(
            "monorepo", "https://gitlab.example.com/group/monorepo.git", RepositoryProvider.GitLab, null, null, null, null));

        var result = await sut.DiscoverApplicationsAsync(repo.Id, "develop");

        Assert.False(result.Success);
        Assert.Equal("develop", result.Branch);
        Assert.Empty(result.Folders);
        Assert.Contains("401", result.ErrorMessage);
    }

    [Fact]
    public async Task DiscoverApplicationsAsync_WithNoBranchOrDefaultBranch_FallsBackToMain()
    {
        var gitClient = new FakeGitProviderClient(foldersResult: GitProviderResult<IReadOnlyList<DevOpsPortal.Application.Abstractions.GitRepositoryFolder>>.Ok([]));
        var sut = CreateSut(out _, gitClient);
        var repo = await sut.CreateAsync(new CreateRepositoryRequest(
            "monorepo", "https://gitlab.example.com/group/monorepo.git", RepositoryProvider.GitLab, null, null, null, null));

        var result = await sut.DiscoverApplicationsAsync(repo.Id, null);

        Assert.Equal("main", result.Branch);
    }
}
