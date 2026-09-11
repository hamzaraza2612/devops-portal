using DevOpsPortal.Application.Abstractions;
using DevOpsPortal.Application.Dtos.Builds;
using DevOpsPortal.Application.Exceptions;
using DevOpsPortal.Application.Services;
using DevOpsPortal.Domain.Constants;
using DevOpsPortal.Domain.Entities;
using DevOpsPortal.Domain.Enums;
using DevOpsPortal.Infrastructure.Persistence;
using DevOpsPortal.Tests.Common;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DevOpsPortal.Tests.Services;

public class BuildServiceTests
{
    private sealed record Fixture(
        BuildService Sut,
        AppDbContext Db,
        FakeCurrentUserService CurrentUser,
        ManagedApplication App,
        BuildServer BuildServer,
        Guid RequestUserId,
        Guid ViewOnlyUserId,
        Guid NoPermissionUserId);

    private static async Task<Fixture> CreateFixtureAsync(
        IBuildProvider? provider = null, bool configureBuildConfig = true, ImageTagStrategy tagStrategy = ImageTagStrategy.CommitSha)
    {
        var db = TestDb.CreateInMemory();
        await TestDb.SeedRolesAndPermissionsAsync(db);

        var app = new ManagedApplication { Name = "Sample", Slug = "sample", DeploymentMode = DeploymentMode.ContainerImage, IsActive = true };
        db.Applications.Add(app);

        var buildServer = new BuildServer { Name = "jenkins-main", BaseUrl = "https://jenkins.example.local", ProviderType = BuildProviderType.Jenkins, IsActive = true };
        db.BuildServers.Add(buildServer);
        await db.SaveChangesAsync();

        if (configureBuildConfig)
        {
            db.BuildConfigurations.Add(new BuildConfiguration
            {
                ApplicationId = app.Id,
                BuildServerId = buildServer.Id,
                JobName = "sample-app-build",
                ImageRegistry = "registry.example.com",
                ImageRepository = "group/sample",
                ImageTagStrategy = tagStrategy,
            });
            await db.SaveChangesAsync();
        }

        var currentUser = new FakeCurrentUserService();
        var currentTenant = new FakeCurrentTenantService();
        var audit = new AuditService(db, currentUser, currentTenant);
        var providers = new List<IBuildProvider> { provider ?? new FakeBuildProvider(BuildProviderType.Jenkins) };
        var sut = new BuildService(db, currentUser, currentTenant, audit, providers);

        var requestUserId = await TestDb.CreateUserWithPermissionsAsync(db, "builder", PermissionCodes.BuildsView, PermissionCodes.BuildsRequest);
        var viewOnlyUserId = await TestDb.CreateUserWithPermissionsAsync(db, "viewer", PermissionCodes.BuildsView);
        var noPermissionUserId = await TestDb.CreateUserWithPermissionsAsync(db, "nobody", PermissionCodes.UsersView);

        return new Fixture(sut, db, currentUser, app, buildServer, requestUserId, viewOnlyUserId, noPermissionUserId);
    }

    // -------------------------------------------------------------- authorization

    [Fact]
    public async Task RequestBuildAsync_WithoutBuildsRequestPermission_ThrowsForbidden()
    {
        var f = await CreateFixtureAsync();
        f.CurrentUser.UserId = f.NoPermissionUserId;

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            f.Sut.RequestBuildAsync(f.App.Id, new RequestBuildRequest("main", "abc123", null)));
    }

    [Fact]
    public async Task RequestBuildAsync_ViewOnlyPermission_ThrowsForbidden_UnauthorizedApplication()
    {
        var f = await CreateFixtureAsync();
        f.CurrentUser.UserId = f.ViewOnlyUserId;

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            f.Sut.RequestBuildAsync(f.App.Id, new RequestBuildRequest("main", "abc123", null)));
    }

    [Fact]
    public async Task GetAsync_WithoutBuildsViewPermission_ThrowsForbidden()
    {
        var f = await CreateFixtureAsync();
        f.CurrentUser.UserId = f.RequestUserId;
        var created = await f.Sut.RequestBuildAsync(f.App.Id, new RequestBuildRequest("main", "abc123", null));

        f.CurrentUser.UserId = f.NoPermissionUserId;
        await Assert.ThrowsAsync<ForbiddenException>(() => f.Sut.GetAsync(created.Id));
    }

    // -------------------------------------------------------------- request validation

    [Fact]
    public async Task RequestBuildAsync_WithNoBuildConfiguration_ThrowsValidation()
    {
        var f = await CreateFixtureAsync(configureBuildConfig: false);
        f.CurrentUser.UserId = f.RequestUserId;

        await Assert.ThrowsAsync<ValidationException>(() =>
            f.Sut.RequestBuildAsync(f.App.Id, new RequestBuildRequest("main", "abc123", null)));
    }

    [Fact]
    public async Task RequestBuildAsync_UnknownApplication_ThrowsNotFound()
    {
        var f = await CreateFixtureAsync();
        f.CurrentUser.UserId = f.RequestUserId;

        await Assert.ThrowsAsync<NotFoundException>(() =>
            f.Sut.RequestBuildAsync(Guid.NewGuid(), new RequestBuildRequest("main", "abc123", null)));
    }

    [Fact]
    public async Task RequestBuildAsync_SemVerStrategyWithoutSemVer_ThrowsValidation()
    {
        var f = await CreateFixtureAsync(tagStrategy: ImageTagStrategy.SemVer);
        f.CurrentUser.UserId = f.RequestUserId;

        await Assert.ThrowsAsync<ValidationException>(() =>
            f.Sut.RequestBuildAsync(f.App.Id, new RequestBuildRequest("main", "abc123", null)));
    }

    // -------------------------------------------------------------- successful build

    [Fact]
    public async Task RequestBuildAsync_WhenTriggerSucceeds_PersistsQueuedRequest_NeverDeploys()
    {
        var f = await CreateFixtureAsync();
        f.CurrentUser.UserId = f.RequestUserId;

        var dto = await f.Sut.RequestBuildAsync(f.App.Id, new RequestBuildRequest("main", "abc123def456", null));

        Assert.Equal(BuildStatus.Queued, dto.Status);
        Assert.Equal("abc123def456", dto.CommitSha);
        Assert.Equal("main", dto.Branch);
        Assert.Equal("sample-app-build", dto.JobName);
        Assert.Equal(0, await f.Db.Deployments.CountAsync());
    }

    [Fact]
    public async Task GetAsync_WhenBuildResolvesToSuccess_CreatesReleaseWithTraceableImageReference()
    {
        var provider = new FakeBuildProvider(BuildProviderType.Jenkins,
            status: () => BuildStatusResult.Ok(ProviderBuildLifecycleState.Succeeded, 17, "https://jenkins.example.local/job/sample-app-build/17/"));
        var f = await CreateFixtureAsync(provider);
        f.CurrentUser.UserId = f.RequestUserId;

        var created = await f.Sut.RequestBuildAsync(f.App.Id, new RequestBuildRequest("main", "abc123def456", null));
        var refreshed = await f.Sut.GetAsync(created.Id);

        Assert.Equal(BuildStatus.Succeeded, refreshed.Status);
        Assert.Equal(17, refreshed.BuildNumber);
        Assert.NotNull(refreshed.ReleaseId);
        Assert.Equal("registry.example.com/group/sample:abc123def456", refreshed.ImageReference);

        var release = await f.Db.Releases.SingleAsync(r => r.Id == refreshed.ReleaseId);
        Assert.Equal("abc123def456", release.CommitSha);
        Assert.Equal(17, release.BuildNumber);
        Assert.Equal(BuildStatus.Succeeded, release.BuildStatus);
    }

    [Fact]
    public async Task GetAsync_WhenBuildResolvesToSuccess_UsesBuildNumberTag_WhenCommitStrategyButNoCommitSupplied()
    {
        var provider = new FakeBuildProvider(BuildProviderType.Jenkins,
            status: () => BuildStatusResult.Ok(ProviderBuildLifecycleState.Succeeded, 23, null));
        var f = await CreateFixtureAsync(provider);
        f.CurrentUser.UserId = f.RequestUserId;

        var created = await f.Sut.RequestBuildAsync(f.App.Id, new RequestBuildRequest("main", null, null));
        var refreshed = await f.Sut.GetAsync(created.Id);

        Assert.Equal("registry.example.com/group/sample:23", refreshed.ImageReference);
    }

    // ----------------------------------------------------------------- failed build

    [Fact]
    public async Task RequestBuildAsync_WhenTriggerFails_PersistsFailedRequest_WithErrorMessage()
    {
        var provider = new FakeBuildProvider(BuildProviderType.Jenkins, trigger: _ => BuildTriggerResult.Fail("Jenkins returned 401 Unauthorized."));
        var f = await CreateFixtureAsync(provider);
        f.CurrentUser.UserId = f.RequestUserId;

        var dto = await f.Sut.RequestBuildAsync(f.App.Id, new RequestBuildRequest("main", "abc123", null));

        Assert.Equal(BuildStatus.Failed, dto.Status);
        Assert.Contains("401", dto.ErrorMessage);
        Assert.NotNull(dto.CompletedAt);

        var auditEntry = await f.Db.AuditLogs.SingleAsync(a => a.Action == "build.request");
        Assert.Equal(AuditResult.Failure, auditEntry.Result);
    }

    [Fact]
    public async Task GetAsync_WhenBuildResolvesToFailure_MarksFailed_NoReleaseCreated()
    {
        var provider = new FakeBuildProvider(BuildProviderType.Jenkins,
            status: () => BuildStatusResult.Ok(ProviderBuildLifecycleState.Failed, 3, null));
        var f = await CreateFixtureAsync(provider);
        f.CurrentUser.UserId = f.RequestUserId;

        var created = await f.Sut.RequestBuildAsync(f.App.Id, new RequestBuildRequest("main", "abc123", null));
        var refreshed = await f.Sut.GetAsync(created.Id);

        Assert.Equal(BuildStatus.Failed, refreshed.Status);
        Assert.Null(refreshed.ReleaseId);
        Assert.Equal(0, await f.Db.Releases.CountAsync());
    }

    // -------------------------------------------------------------- invalid provider

    [Fact]
    public async Task RequestBuildAsync_WhenNoProviderRegisteredForType_PersistsFailedRequest_NeverThrows()
    {
        var db = TestDb.CreateInMemory();
        await TestDb.SeedRolesAndPermissionsAsync(db);

        var app = new ManagedApplication { Name = "Sample", Slug = "sample", DeploymentMode = DeploymentMode.ContainerImage, IsActive = true };
        db.Applications.Add(app);
        var buildServer = new BuildServer { Name = "jenkins-main", BaseUrl = "https://jenkins.example.local", ProviderType = BuildProviderType.Jenkins, IsActive = true };
        db.BuildServers.Add(buildServer);
        await db.SaveChangesAsync();
        db.BuildConfigurations.Add(new BuildConfiguration { ApplicationId = app.Id, BuildServerId = buildServer.Id, JobName = "job" });
        await db.SaveChangesAsync();

        var currentUser = new FakeCurrentUserService();
        var currentTenant = new FakeCurrentTenantService();
        // No IBuildProvider registered at all — simulates a BuildServer whose
        // ProviderType has no matching implementation (invalid provider).
        var sut = new BuildService(db, currentUser, currentTenant, new AuditService(db, currentUser, currentTenant), []);
        currentUser.UserId = await TestDb.CreateUserWithPermissionsAsync(db, "builder", PermissionCodes.BuildsView, PermissionCodes.BuildsRequest);

        var dto = await sut.RequestBuildAsync(app.Id, new RequestBuildRequest(null, null, null));

        Assert.Equal(BuildStatus.Failed, dto.Status);
        Assert.Contains("No build provider is registered", dto.ErrorMessage);
    }

    // ------------------------------------------------------------------- logs

    [Fact]
    public async Task GetLogAsync_WhenBuildNumberUnknown_ReturnsUnavailable()
    {
        var f = await CreateFixtureAsync();
        f.CurrentUser.UserId = f.RequestUserId;
        var created = await f.Sut.RequestBuildAsync(f.App.Id, new RequestBuildRequest("main", "abc123", null));

        var log = await f.Sut.GetLogAsync(created.Id);

        Assert.False(log.Available);
    }

    [Fact]
    public async Task GetLogAsync_SanitizesSecretsFromProviderLogText()
    {
        var provider = new FakeBuildProvider(BuildProviderType.Jenkins,
            status: () => BuildStatusResult.Ok(ProviderBuildLifecycleState.Running, 4, null),
            log: () => BuildLogResult.Ok("Step 1 ok\nAPI_TOKEN=supersecretvalue\nStep 2 ok", false));
        var f = await CreateFixtureAsync(provider);
        f.CurrentUser.UserId = f.RequestUserId;
        var created = await f.Sut.RequestBuildAsync(f.App.Id, new RequestBuildRequest("main", "abc123", null));
        await f.Sut.GetAsync(created.Id); // resolve BuildNumber

        var log = await f.Sut.GetLogAsync(created.Id);

        Assert.True(log.Available);
        Assert.DoesNotContain("supersecretvalue", log.LogText);
        Assert.Contains("REDACTED", log.LogText);
    }

    private sealed class FakeBuildProvider(
        BuildProviderType providerType,
        Func<BuildTriggerRequest, BuildTriggerResult>? trigger = null,
        Func<BuildStatusResult>? status = null,
        Func<BuildLogResult>? log = null) : IBuildProvider
    {
        public BuildProviderType ProviderType => providerType;

        public bool IsConfigured(BuildServer buildServer) => true;

        public Task<BuildTriggerResult> TriggerBuildAsync(BuildServer buildServer, BuildTriggerRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(trigger?.Invoke(request) ?? BuildTriggerResult.Ok("queue-1"));

        public Task<BuildStatusResult> GetBuildStatusAsync(
            BuildServer buildServer, string jobName, string? providerQueueItemId, int? knownBuildNumber, CancellationToken cancellationToken = default) =>
            Task.FromResult(status?.Invoke() ?? BuildStatusResult.Ok(ProviderBuildLifecycleState.Running, knownBuildNumber, null));

        public Task<BuildLogResult> GetBuildLogAsync(BuildServer buildServer, string jobName, int buildNumber, CancellationToken cancellationToken = default) =>
            Task.FromResult(log?.Invoke() ?? BuildLogResult.Ok("log output", true));
    }
}
