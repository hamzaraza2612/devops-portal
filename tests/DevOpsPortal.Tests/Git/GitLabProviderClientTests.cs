using System.Net;
using System.Text;
using System.Text.Json;
using DevOpsPortal.Domain.Entities;
using DevOpsPortal.Domain.Enums;
using DevOpsPortal.Infrastructure.Git;
using DevOpsPortal.Tests.Common;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DevOpsPortal.Tests.Git;

public class GitLabProviderClientTests
{
    private static GitLabProviderClient CreateSut() =>
        new(new HttpClient(), new FakeSecretProvider(), NullLogger<GitLabProviderClient>.Instance);

    private static GitLabProviderClient CreateSut(HttpMessageHandler handler) =>
        new(new HttpClient(handler), new FakeSecretProvider(), NullLogger<GitLabProviderClient>.Instance);

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

    [Fact]
    public async Task DownloadRepositoryArchiveAsync_WithInvalidRepositoryUrl_ReturnsFail()
    {
        var sut = CreateSut();
        var repository = new Repository { Name = "bad-url", Url = "not-a-url", Provider = RepositoryProvider.GitLab };

        var result = await sut.DownloadRepositoryArchiveAsync(repository, "main");

        Assert.False(result.Success);
        Assert.NotNull(result.ErrorMessage);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task DownloadRepositoryArchiveAsync_WithBlankRef_ReturnsFail(string refName)
    {
        var sut = CreateSut();
        var repository = new Repository { Name = "ok", Url = "https://gitlab.example.com/group/app", Provider = RepositoryProvider.GitLab };

        var result = await sut.DownloadRepositoryArchiveAsync(repository, refName);

        Assert.False(result.Success);
        Assert.Contains("branch name or commit SHA", result.ErrorMessage);
    }

    [Fact]
    public async Task ListRepositoryFoldersAsync_WithInvalidRepositoryUrl_ReturnsFail()
    {
        var sut = CreateSut();
        var repository = new Repository { Name = "bad-url", Url = "not-a-url", Provider = RepositoryProvider.GitLab };

        var result = await sut.ListRepositoryFoldersAsync(repository, "main");

        Assert.False(result.Success);
        Assert.NotNull(result.ErrorMessage);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ListRepositoryFoldersAsync_WithBlankRef_ReturnsFail(string refName)
    {
        var sut = CreateSut();
        var repository = new Repository { Name = "ok", Url = "https://gitlab.example.com/group/app", Provider = RepositoryProvider.GitLab };

        var result = await sut.ListRepositoryFoldersAsync(repository, refName);

        Assert.False(result.Success);
        Assert.Contains("branch name or commit SHA", result.ErrorMessage);
    }

    [Fact]
    public async Task ListRepositoryFoldersAsync_WhenHostUnreachable_ReturnsFailNeverThrows()
    {
        var sut = CreateSut();
        var repository = new Repository
        {
            Name = "unreachable",
            Url = "http://127.0.0.1:1/group/app",
            Provider = RepositoryProvider.GitLab,
        };

        var result = await sut.ListRepositoryFoldersAsync(repository, "main");

        Assert.False(result.Success);
        Assert.NotNull(result.ErrorMessage);
    }

    [Fact]
    public async Task DownloadRepositoryArchiveAsync_WhenHostUnreachable_ReturnsFailNeverThrows()
    {
        var sut = CreateSut();
        var repository = new Repository
        {
            Name = "unreachable",
            Url = "http://127.0.0.1:1/group/app",
            Provider = RepositoryProvider.GitLab,
        };

        var result = await sut.DownloadRepositoryArchiveAsync(repository, "main");

        Assert.False(result.Success);
        Assert.NotNull(result.ErrorMessage);
    }

    /// <summary>Regression test for a real production bug: a first version of
    /// ListRepositoryFoldersAsync used one `recursive=true` tree call. GitLab
    /// walks a recursive tree depth-first, so a folder's own docker-compose.yml
    /// (a direct sibling of e.g. a `Backups/` subdirectory) can sit behind an
    /// arbitrarily large recursively-listed subtree — on a real monorepo where
    /// every application folder has a `Backups/` directory full of historical
    /// deployment snapshots, this caused every single folder to report "no
    /// compose file found" because the scan's page cap was exhausted deep
    /// inside the first folder's Backups tree before ever reaching a compose
    /// file. This test proves the fix (non-recursive top-level listing + one
    /// non-recursive direct-children listing per folder) never even requests
    /// Backups'/publish's contents, so it cannot get lost in them however large
    /// they are.</summary>
    [Fact]
    public async Task ListRepositoryFoldersAsync_FindsComposeFileEvenWhenItsSiblingFolderIsHuge_NeverRecursingIntoSiblings()
    {
        var requestedUrls = new List<string>();
        var handler = new FakeHttpMessageHandler(request =>
        {
            var url = request.RequestUri!.ToString();
            requestedUrls.Add(url);

            // Never allowed to ask for a recursive listing again — that's the exact bug being guarded against.
            Assert.DoesNotContain("recursive=true", url);

            if (!url.Contains("path="))
            {
                // Top-level listing: two application folders, no files (matches a real repo root).
                return JsonResponse([
                    new { id = "1", name = "dmsapi", type = "tree", path = "dmsapi" },
                    new { id = "2", name = "emptyapp", type = "tree", path = "emptyapp" },
                ]);
            }

            if (url.Contains("path=dmsapi"))
            {
                // dmsapi's own direct children only — Backups/publish are NOT expanded here,
                // however many thousands of files they might recursively contain on a real repo.
                return JsonResponse([
                    new { id = "3", name = "Backups", type = "tree", path = "dmsapi/Backups" },
                    new { id = "4", name = "docker-compose.yml", type = "blob", path = "dmsapi/docker-compose.yml" },
                    new { id = "5", name = "publish", type = "tree", path = "dmsapi/publish" },
                ]);
            }

            if (url.Contains("path=emptyapp"))
            {
                return JsonResponse([
                    new { id = "6", name = "readme.md", type = "blob", path = "emptyapp/readme.md" },
                ]);
            }

            throw new InvalidOperationException($"Unexpected request: {url}");
        });
        var sut = CreateSut(handler);
        var repository = new Repository { Name = "monorepo", Url = "https://gitlab.example.com/group/monorepo", Provider = RepositoryProvider.GitLab };

        var result = await sut.ListRepositoryFoldersAsync(repository, "DEV");

        Assert.True(result.Success);
        Assert.Equal(2, result.Data!.Count);
        var dmsapi = result.Data!.Single(f => f.Path == "dmsapi");
        Assert.True(dmsapi.HasComposeFile);
        var emptyapp = result.Data!.Single(f => f.Path == "emptyapp");
        Assert.False(emptyapp.HasComposeFile);

        // Exactly 3 requests: the top-level scan plus one direct-children scan per folder —
        // never one for each of Backups/publish, and never a recursive whole-tree call.
        Assert.Equal(3, requestedUrls.Count);
    }

    /// <summary>Regression test for a real production bug: HttpClient.Timeout for this
    /// client was hardcoded to 10s, which is fine for small calls (commit lookups, tree
    /// listings) but was cutting off every real archive download for a real monorepo
    /// (large due to committed deployment Backups/ history), surfacing as the misleading
    /// "Could not reach the configured GitLab instance." — indistinguishable from GitLab
    /// actually being down, even though discovery/commit-lookup calls against the exact
    /// same GitLab instance succeeded moments earlier. This proves a client-side timeout
    /// (simulated with a 1ms HttpClient.Timeout against a handler that never completes)
    /// now gets its own distinct message instead of the generic unreachable one.</summary>
    [Fact]
    public async Task DownloadRepositoryArchiveAsync_WhenClientTimesOut_ReturnsTimeoutSpecificMessage_NotGenericUnreachable()
    {
        var handler = new FakeHttpMessageHandler(_ => throw new TaskCanceledException(
            "The request was canceled due to the configured HttpClient.Timeout.",
            new TimeoutException()));
        var httpClient = new HttpClient(handler) { Timeout = TimeSpan.FromMilliseconds(1) };
        var sut = new GitLabProviderClient(httpClient, new FakeSecretProvider(), NullLogger<GitLabProviderClient>.Instance);
        var repository = new Repository { Name = "monorepo", Url = "https://gitlab.example.com/group/monorepo", Provider = RepositoryProvider.GitLab };

        var result = await sut.DownloadRepositoryArchiveAsync(repository, "DEV");

        Assert.False(result.Success);
        Assert.Contains("timed out", result.ErrorMessage);
        Assert.DoesNotContain("Could not reach", result.ErrorMessage);
    }

    private static HttpResponseMessage JsonResponse(IReadOnlyList<object> entries) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(JsonSerializer.Serialize(entries), Encoding.UTF8, "application/json"),
    };

    private sealed class FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }
}
