using DevOpsPortal.Domain.Entities;
using DevOpsPortal.Domain.Enums;
using DevOpsPortal.Infrastructure.Git;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DevOpsPortal.Tests.Git;

public class GitLabProviderClientTests
{
    private static GitLabProviderClient CreateSut() =>
        new(new HttpClient(), NullLogger<GitLabProviderClient>.Instance);

    [Fact]
    public async Task GetLatestCommitAsync_WithInvalidRepositoryUrl_ReturnsFail()
    {
        var sut = CreateSut();
        var repository = new Repository { Name = "bad-url", Url = "not-a-url", Provider = RepositoryProvider.GitLab };

        var result = await sut.GetLatestCommitAsync(repository, "main");

        Assert.False(result.Success);
        Assert.NotNull(result.ErrorMessage);
    }

    [Fact]
    public async Task GetLatestCommitAsync_WithEmptyProjectPath_ReturnsFail()
    {
        var sut = CreateSut();
        var repository = new Repository { Name = "no-path", Url = "https://gitlab.example.com/", Provider = RepositoryProvider.GitLab };

        var result = await sut.GetLatestCommitAsync(repository, "main");

        Assert.False(result.Success);
        Assert.Contains("project path", result.ErrorMessage);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task GetRecentCommitsAsync_WithBlankBranch_ReturnsFail(string branch)
    {
        var sut = CreateSut();
        var repository = new Repository { Name = "ok", Url = "https://gitlab.example.com/group/app", Provider = RepositoryProvider.GitLab };

        var result = await sut.GetRecentCommitsAsync(repository, branch, 5);

        Assert.False(result.Success);
        Assert.Contains("branch", result.ErrorMessage);
    }

    [Fact]
    public async Task GetRecentCommitsAsync_WhenHostUnreachable_ReturnsFailNeverThrows()
    {
        var sut = CreateSut();
        var repository = new Repository
        {
            Name = "unreachable",
            Url = "http://127.0.0.1:1/group/app",
            Provider = RepositoryProvider.GitLab,
        };

        var result = await sut.GetRecentCommitsAsync(repository, "main", 5);

        Assert.False(result.Success);
        Assert.NotNull(result.ErrorMessage);
    }
}
