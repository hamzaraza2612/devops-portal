using DevOpsPortal.Application.Abstractions;
using DevOpsPortal.Application.Dtos.Containers;
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

public class ContainerOperationsServiceTests
{
    private sealed record Fixture(
        ContainerOperationsService Sut,
        AppDbContext Db,
        FakeCurrentUserService CurrentUser,
        ManagedApplication App,
        EnvironmentDefinition DevEnv,
        ApplicationEnvironment AppEnv,
        Guid ViewUserId,
        Guid ControlUserId,
        Guid RecreateUserId,
        Guid NoPermissionUserId);

    private static async Task<Fixture> CreateFixtureAsync(
        IContainerInspector? inspector = null,
        IComposeCommandExecutor? composeExecutor = null,
        IHealthCheckProbe? healthProbe = null,
        bool useDownWithVolumesOnDeploy = false,
        bool appEnvActive = true)
    {
        var db = TestDb.CreateInMemory();
        await TestDb.SeedRolesAndPermissionsAsync(db);
        await TestDb.SeedEnvironmentDefinitionsAsync(db);

        var app = new ManagedApplication { Name = "Sample", Slug = "sample", DeploymentMode = DeploymentMode.LegacyFilesystem };
        db.Applications.Add(app);

        var server = new TargetServer { Name = "server-1", IsActive = true };
        server.AllowedDeploymentRoots.Add(new AllowedDeploymentRoot { RootPath = "/tmp", IsActive = true, TargetServerId = server.Id });
        db.TargetServers.Add(server);
        await db.SaveChangesAsync();

        var devEnv = db.EnvironmentDefinitions.Single(e => e.Name == EnvironmentNames.Dev);

        var appEnv = new ApplicationEnvironment
        {
            ApplicationId = app.Id,
            EnvironmentDefinitionId = devEnv.Id,
            TargetServerId = server.Id,
            DeploymentRootPath = "/tmp",
            ComposeFilePath = "docker-compose.yml",
            ServiceName = "web",
            IsActive = appEnvActive,
            HealthCheckType = HealthCheckType.None,
            UseDownWithVolumesOnDeploy = useDownWithVolumesOnDeploy,
        };
        db.ApplicationEnvironments.Add(appEnv);
        await db.SaveChangesAsync();

        var currentUser = new FakeCurrentUserService();
        var audit = new AuditService(db, currentUser);
        var sut = new ContainerOperationsService(
            db, currentUser, audit,
            inspector ?? new FakeContainerInspector([]),
            composeExecutor ?? new FakeComposeCommandExecutor(true),
            healthProbe ?? new FakeHealthCheckProbe(true));

        var viewUserId = await TestDb.CreateUserWithPermissionsAsync(db, "viewer", PermissionCodes.ContainersView);
        var controlUserId = await TestDb.CreateUserWithPermissionsAsync(
            db, "controller", PermissionCodes.ContainersView, PermissionCodes.ContainersControl);
        var recreateUserId = await TestDb.CreateUserWithPermissionsAsync(
            db, "recreator", PermissionCodes.ContainersView, PermissionCodes.ContainersControl, PermissionCodes.ContainersRecreate);
        var noPermissionUserId = await TestDb.CreateUserWithPermissionsAsync(db, "nobody", PermissionCodes.UsersView);

        return new Fixture(sut, db, currentUser, app, devEnv, appEnv, viewUserId, controlUserId, recreateUserId, noPermissionUserId);
    }

    // ------------------------------------------------------------- status retrieval

    [Fact]
    public async Task GetStatusAsync_WithConfiguredEnvironment_ReturnsContainersAndHealthCheck()
    {
        var container = new ContainerStatusInfo("web", "sample-web-1", "sample", "abc1234", ContainerState.Running, "healthy", DateTimeOffset.UtcNow.AddMinutes(-10), 0, ["8080->80/tcp"]);
        var f = await CreateFixtureAsync(inspector: new FakeContainerInspector([container]), healthProbe: new FakeHealthCheckProbe(true, "HTTP 200 OK"));
        f.CurrentUser.UserId = f.ViewUserId;

        var status = await f.Sut.GetStatusAsync(f.App.Id, f.DevEnv.Id);

        Assert.True(status.IsConfigured);
        var c = Assert.Single(status.Containers);
        Assert.Equal("web", c.ServiceName);
        Assert.Equal(ContainerState.Running, c.State);
        Assert.NotNull(c.Uptime);
        Assert.Equal("sample:abc1234", status.CurrentImageOrVersion);
    }

    [Fact]
    public async Task GetStatusAsync_WhenContainerReportsUnhealthy_ReflectsUnhealthyState()
    {
        var container = new ContainerStatusInfo("web", "sample-web-1", "sample", "abc1234", ContainerState.Unhealthy, "unhealthy", DateTimeOffset.UtcNow, 3, []);
        var f = await CreateFixtureAsync(inspector: new FakeContainerInspector([container]));
        f.CurrentUser.UserId = f.ViewUserId;

        var status = await f.Sut.GetStatusAsync(f.App.Id, f.DevEnv.Id);

        var c = Assert.Single(status.Containers);
        Assert.Equal(ContainerState.Unhealthy, c.State);
        Assert.Equal(3, c.RestartCount);
    }

    [Fact]
    public async Task GetStatusAsync_WhenApplicationEnvironmentNotConfigured_ReturnsIsConfiguredFalse()
    {
        var f = await CreateFixtureAsync();
        f.CurrentUser.UserId = f.ViewUserId;
        var prodEnv = await f.Db.EnvironmentDefinitions.SingleAsync(e => e.Name == EnvironmentNames.Production);

        var status = await f.Sut.GetStatusAsync(f.App.Id, prodEnv.Id);

        Assert.False(status.IsConfigured);
        Assert.Empty(status.Containers);
    }

    [Fact]
    public async Task GetStatusAsync_UnknownApplication_ThrowsNotFound()
    {
        var f = await CreateFixtureAsync();
        f.CurrentUser.UserId = f.ViewUserId;

        await Assert.ThrowsAsync<NotFoundException>(() => f.Sut.GetStatusAsync(Guid.NewGuid(), f.DevEnv.Id));
    }

    [Fact]
    public async Task GetStatusAsync_WithoutContainersViewPermission_ThrowsForbidden()
    {
        var f = await CreateFixtureAsync();
        f.CurrentUser.UserId = f.NoPermissionUserId;

        await Assert.ThrowsAsync<ForbiddenException>(() => f.Sut.GetStatusAsync(f.App.Id, f.DevEnv.Id));
    }

    // ---------------------------------------------------------- restart/start/stop

    [Fact]
    public async Task RestartAsync_WithoutControlPermission_ThrowsForbidden_AndNeverInvokesCompose()
    {
        var composeExecutor = new FakeComposeCommandExecutor(true);
        var f = await CreateFixtureAsync(composeExecutor: composeExecutor);
        f.CurrentUser.UserId = f.ViewUserId; // holds View but not Control

        await Assert.ThrowsAsync<ForbiddenException>(() => f.Sut.RestartAsync(f.App.Id, f.DevEnv.Id));
        Assert.Equal(0, composeExecutor.InvocationCount);
    }

    [Fact]
    public async Task RestartAsync_WithControlPermission_Succeeds_AndRecordsSuccessAudit()
    {
        var f = await CreateFixtureAsync(composeExecutor: new FakeComposeCommandExecutor(true, output: "Restarting sample-web-1..."));
        f.CurrentUser.UserId = f.ControlUserId;

        var result = await f.Sut.RestartAsync(f.App.Id, f.DevEnv.Id);

        Assert.True(result.Success);
        var auditEntry = await f.Db.AuditLogs.SingleAsync(a => a.Action == "container.restart");
        Assert.Equal(AuditResult.Success, auditEntry.Result);
        Assert.Contains("Sample", auditEntry.Details);
    }

    [Fact]
    public async Task StartAsync_InvokesComposeStartOperation()
    {
        var composeExecutor = new FakeComposeCommandExecutor(true);
        var f = await CreateFixtureAsync(composeExecutor: composeExecutor);
        f.CurrentUser.UserId = f.ControlUserId;

        await f.Sut.StartAsync(f.App.Id, f.DevEnv.Id);

        Assert.Equal(ComposeOperation.Start, composeExecutor.LastOperation);
    }

    [Fact]
    public async Task StopAsync_InvokesComposeStopOperation()
    {
        var composeExecutor = new FakeComposeCommandExecutor(true);
        var f = await CreateFixtureAsync(composeExecutor: composeExecutor);
        f.CurrentUser.UserId = f.ControlUserId;

        await f.Sut.StopAsync(f.App.Id, f.DevEnv.Id);

        Assert.Equal(ComposeOperation.Stop, composeExecutor.LastOperation);
    }

    [Fact]
    public async Task RestartAsync_WhenComposeFails_ReturnsFailureResult_AndRecordsFailureAudit_WithoutThrowing()
    {
        var f = await CreateFixtureAsync(composeExecutor: new FakeComposeCommandExecutor(false, error: "cannot connect to the Docker daemon"));
        f.CurrentUser.UserId = f.ControlUserId;

        var result = await f.Sut.RestartAsync(f.App.Id, f.DevEnv.Id);

        Assert.False(result.Success);
        Assert.Contains("Docker daemon", result.Message);
        var auditEntry = await f.Db.AuditLogs.SingleAsync(a => a.Action == "container.restart");
        Assert.Equal(AuditResult.Failure, auditEntry.Result);
    }

    [Fact]
    public async Task RestartAsync_SanitizesSecretsFromComposeOutputBeforeAuditing()
    {
        var f = await CreateFixtureAsync(composeExecutor: new FakeComposeCommandExecutor(true, output: "DB_PASSWORD=hunter2 restart complete"));
        f.CurrentUser.UserId = f.ControlUserId;

        await f.Sut.RestartAsync(f.App.Id, f.DevEnv.Id);

        var auditEntry = await f.Db.AuditLogs.SingleAsync(a => a.Action == "container.restart");
        Assert.DoesNotContain("hunter2", auditEntry.Details);
    }

    [Fact]
    public async Task RestartAsync_UnconfiguredEnvironment_ThrowsValidation()
    {
        var f = await CreateFixtureAsync();
        f.CurrentUser.UserId = f.ControlUserId;
        var prodEnv = await f.Db.EnvironmentDefinitions.SingleAsync(e => e.Name == EnvironmentNames.Production);

        await Assert.ThrowsAsync<ValidationException>(() => f.Sut.RestartAsync(f.App.Id, prodEnv.Id));
    }

    [Fact]
    public async Task RestartAsync_UnknownApplication_ThrowsNotFound()
    {
        var f = await CreateFixtureAsync();
        f.CurrentUser.UserId = f.ControlUserId;

        await Assert.ThrowsAsync<NotFoundException>(() => f.Sut.RestartAsync(Guid.NewGuid(), f.DevEnv.Id));
    }

    // -------------------------------------------------------- recreate (down -v / up -d)

    [Fact]
    public async Task RecreateWithVolumesAsync_WithoutRecreatePermission_ThrowsForbidden()
    {
        var f = await CreateFixtureAsync(useDownWithVolumesOnDeploy: true);
        f.CurrentUser.UserId = f.ControlUserId; // holds Control but not Recreate

        await Assert.ThrowsAsync<ForbiddenException>(() => f.Sut.RecreateWithVolumesAsync(f.App.Id, f.DevEnv.Id, new RecreateWithVolumesRequest(true)));
    }

    [Fact]
    public async Task RecreateWithVolumesAsync_WithoutConfirmFlag_ThrowsValidation_AndNeverInvokesCompose()
    {
        var composeExecutor = new FakeComposeCommandExecutor(true);
        var f = await CreateFixtureAsync(composeExecutor: composeExecutor, useDownWithVolumesOnDeploy: true);
        f.CurrentUser.UserId = f.RecreateUserId;

        await Assert.ThrowsAsync<ValidationException>(() => f.Sut.RecreateWithVolumesAsync(f.App.Id, f.DevEnv.Id, new RecreateWithVolumesRequest(false)));
        Assert.Equal(0, composeExecutor.InvocationCount);
    }

    [Fact]
    public async Task RecreateWithVolumesAsync_WhenNotExplicitlyConfigured_ThrowsValidation_RegardlessOfConfirm()
    {
        // UseDownWithVolumesOnDeploy defaults to false — the destructive action must
        // never be available just because the caller holds the permission and confirmed.
        var f = await CreateFixtureAsync(useDownWithVolumesOnDeploy: false);
        f.CurrentUser.UserId = f.RecreateUserId;

        var ex = await Assert.ThrowsAsync<ValidationException>(
            () => f.Sut.RecreateWithVolumesAsync(f.App.Id, f.DevEnv.Id, new RecreateWithVolumesRequest(true)));
        Assert.Contains("UseDownWithVolumesOnDeploy", ex.Message);
    }

    [Fact]
    public async Task RecreateWithVolumesAsync_WhileDeploymentActive_ThrowsConflict()
    {
        var f = await CreateFixtureAsync(useDownWithVolumesOnDeploy: true);
        f.CurrentUser.UserId = f.RecreateUserId;
        f.Db.Deployments.Add(new Deployment
        {
            ApplicationId = f.App.Id,
            EnvironmentDefinitionId = f.DevEnv.Id,
            ApplicationEnvironmentId = f.AppEnv.Id,
            CommitSha = "abc1234",
            Status = DeploymentStatus.Running,
            RequestedByUserId = f.RecreateUserId,
        });
        await f.Db.SaveChangesAsync();

        await Assert.ThrowsAsync<ConflictException>(() => f.Sut.RecreateWithVolumesAsync(f.App.Id, f.DevEnv.Id, new RecreateWithVolumesRequest(true)));
    }

    [Fact]
    public async Task RecreateWithVolumesAsync_WithConfirmAndExplicitOptIn_Succeeds_AndAuditsRequestedAndSucceeded()
    {
        var composeExecutor = new FakeComposeCommandExecutor(true);
        var f = await CreateFixtureAsync(composeExecutor: composeExecutor, useDownWithVolumesOnDeploy: true);
        f.CurrentUser.UserId = f.RecreateUserId;

        var result = await f.Sut.RecreateWithVolumesAsync(f.App.Id, f.DevEnv.Id, new RecreateWithVolumesRequest(true));

        Assert.True(result.Success);
        Assert.Equal([ComposeOperation.DownWithVolumes, ComposeOperation.Up], composeExecutor.OperationsInvoked);

        var requested = await f.Db.AuditLogs.SingleAsync(a => a.Action == "container.recreate.requested");
        Assert.Contains("destroys volumes", requested.Details);
        var succeeded = await f.Db.AuditLogs.SingleAsync(a => a.Action == "container.recreate.succeeded");
        Assert.Equal(AuditResult.Success, succeeded.Result);
    }

    [Fact]
    public async Task RecreateWithVolumesAsync_ProceedsToUpEvenWhenDownFails_MatchingDeployExecutorTolerance()
    {
        var composeExecutor = new FakeComposeCommandExecutor(downSucceeds: false, upSucceeds: true);
        var f = await CreateFixtureAsync(composeExecutor: composeExecutor, useDownWithVolumesOnDeploy: true);
        f.CurrentUser.UserId = f.RecreateUserId;

        var result = await f.Sut.RecreateWithVolumesAsync(f.App.Id, f.DevEnv.Id, new RecreateWithVolumesRequest(true));

        Assert.True(result.Success);
        Assert.Equal([ComposeOperation.DownWithVolumes, ComposeOperation.Up], composeExecutor.OperationsInvoked);
    }

    [Fact]
    public async Task RecreateWithVolumesAsync_WhenUpFails_ReturnsFailure_AndAuditsFailed()
    {
        var composeExecutor = new FakeComposeCommandExecutor(downSucceeds: true, upSucceeds: false);
        var f = await CreateFixtureAsync(composeExecutor: composeExecutor, useDownWithVolumesOnDeploy: true);
        f.CurrentUser.UserId = f.RecreateUserId;

        var result = await f.Sut.RecreateWithVolumesAsync(f.App.Id, f.DevEnv.Id, new RecreateWithVolumesRequest(true));

        Assert.False(result.Success);
        var failed = await f.Db.AuditLogs.SingleAsync(a => a.Action == "container.recreate.failed");
        Assert.Equal(AuditResult.Failure, failed.Result);
    }

    // ---------------------------------------------------------------------- fakes

    private sealed class FakeContainerInspector(IReadOnlyList<ContainerStatusInfo> containers) : IContainerInspector
    {
        public Task<IReadOnlyList<ContainerStatusInfo>> GetStatusAsync(
            string workingDirectory, string composeFilePath, string? projectName, CancellationToken cancellationToken = default) =>
            Task.FromResult(containers);
    }

    private sealed class FakeHealthCheckProbe(bool passed, string detail = "ok") : IHealthCheckProbe
    {
        public Task<HealthCheckResult> ProbeAsync(
            HealthCheckType type, string? endpoint, int timeoutSeconds, CancellationToken cancellationToken = default) =>
            Task.FromResult(new HealthCheckResult(passed, detail));
    }

    private sealed class FakeComposeCommandExecutor : IComposeCommandExecutor
    {
        private readonly bool? _uniformSuccess;
        private readonly bool _downSucceeds;
        private readonly bool _upSucceeds;
        private readonly string _output;
        private readonly string _error;

        public List<ComposeOperation> OperationsInvoked { get; } = [];
        public int InvocationCount => OperationsInvoked.Count;
        public ComposeOperation? LastOperation => OperationsInvoked.Count == 0 ? null : OperationsInvoked[^1];

        public FakeComposeCommandExecutor(bool succeeds, string output = "", string error = "")
        {
            _uniformSuccess = succeeds;
            _output = output;
            _error = error;
        }

        public FakeComposeCommandExecutor(bool downSucceeds, bool upSucceeds)
        {
            _downSucceeds = downSucceeds;
            _upSucceeds = upSucceeds;
            _output = string.Empty;
            _error = string.Empty;
        }

        public Task<ComposeCommandResult> RunAsync(ComposeCommandRequest request, CancellationToken cancellationToken = default)
        {
            OperationsInvoked.Add(request.Operation);
            var success = _uniformSuccess ?? (request.Operation == ComposeOperation.DownWithVolumes ? _downSucceeds : _upSucceeds);
            return Task.FromResult(new ComposeCommandResult(success, success ? 0 : 1, _output, success ? string.Empty : _error));
        }
    }
}
