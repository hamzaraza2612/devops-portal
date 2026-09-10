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
        TargetServer TargetServer,
        Guid ViewUserId,
        Guid ControlUserId,
        Guid RecreateUserId,
        Guid NoPermissionUserId);

    private static async Task<Fixture> CreateFixtureAsync(
        IContainerRuntimeProvider? runtimeProvider = null,
        IHealthCheckProbe? healthProbe = null,
        bool useDownWithVolumesOnDeploy = false,
        bool appEnvActive = true)
    {
        var db = TestDb.CreateInMemory();
        await TestDb.SeedRolesAndPermissionsAsync(db);
        await TestDb.SeedEnvironmentDefinitionsAsync(db);

        var app = new ManagedApplication { Name = "Sample", Slug = "sample", DeploymentMode = DeploymentMode.LegacyFilesystem };
        db.Applications.Add(app);

        var server = new TargetServer { Name = "server-1", Hostname = "10.0.0.5", IsActive = true };
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
            runtimeProvider ?? new FakeContainerRuntimeProvider(),
            healthProbe ?? new FakeHealthCheckProbe(true));

        var viewUserId = await TestDb.CreateUserWithPermissionsAsync(db, "viewer", PermissionCodes.ContainersView);
        var controlUserId = await TestDb.CreateUserWithPermissionsAsync(
            db, "controller", PermissionCodes.ContainersView, PermissionCodes.ContainersControl);
        var recreateUserId = await TestDb.CreateUserWithPermissionsAsync(
            db, "recreator", PermissionCodes.ContainersView, PermissionCodes.ContainersControl, PermissionCodes.ContainersRecreate);
        var noPermissionUserId = await TestDb.CreateUserWithPermissionsAsync(db, "nobody", PermissionCodes.UsersView);

        return new Fixture(sut, db, currentUser, app, devEnv, appEnv, server, viewUserId, controlUserId, recreateUserId, noPermissionUserId);
    }

    // ---------------------------------------------------- reachability (default provider)

    [Fact]
    public async Task GetStatusAsync_WithNoRemoteExecutionConfigured_ReportsUnreachable_NotEmptySuccess()
    {
        // The default fixture provider mirrors NotConfiguredRemoteExecutionProvider's
        // real behavior: every target server is honestly reported unreachable.
        var f = await CreateFixtureAsync();
        f.CurrentUser.UserId = f.ViewUserId;

        var status = await f.Sut.GetStatusAsync(f.App.Id, f.DevEnv.Id);

        Assert.True(status.IsConfigured);
        Assert.False(status.IsReachable);
        Assert.NotNull(status.UnreachableReason);
        Assert.Empty(status.Containers);
        Assert.Equal("server-1", status.TargetServerName);
    }

    [Fact]
    public async Task RestartAsync_WithNoRemoteExecutionConfigured_ReturnsUnreachableFailure_NotAttemptedSuccess()
    {
        var f = await CreateFixtureAsync();
        f.CurrentUser.UserId = f.ControlUserId;

        var result = await f.Sut.RestartAsync(f.App.Id, f.DevEnv.Id);

        Assert.False(result.Success);
        Assert.Contains("No remote execution mechanism is configured", result.Message);
    }

    // ------------------------------------------------------------- status retrieval

    [Fact]
    public async Task GetStatusAsync_WhenTargetServerReachable_ReturnsContainersAndHealthCheck()
    {
        var container = new ContainerStatusInfo("web", "sample-web-1", "sample", "abc1234", ContainerState.Running, "healthy", DateTimeOffset.UtcNow.AddMinutes(-10), 0, ["8080->80/tcp"]);
        var provider = new FakeContainerRuntimeProvider(statusResult: new ContainerRuntimeStatusResult(true, null, [container]));
        var f = await CreateFixtureAsync(runtimeProvider: provider, healthProbe: new FakeHealthCheckProbe(true, "HTTP 200 OK"));
        f.CurrentUser.UserId = f.ViewUserId;

        var status = await f.Sut.GetStatusAsync(f.App.Id, f.DevEnv.Id);

        Assert.True(status.IsReachable);
        var c = Assert.Single(status.Containers);
        Assert.Equal("web", c.ServiceName);
        Assert.Equal(ContainerState.Running, c.State);
        Assert.NotNull(c.Uptime);
        Assert.Equal("sample:abc1234", status.CurrentImageOrVersion);
        Assert.Equal(f.TargetServer.Id, provider.LastGetStatusTargetServer?.Id);
    }

    [Fact]
    public async Task GetStatusAsync_WhenContainerReportsUnhealthy_ReflectsUnhealthyState()
    {
        var container = new ContainerStatusInfo("web", "sample-web-1", "sample", "abc1234", ContainerState.Unhealthy, "unhealthy", DateTimeOffset.UtcNow, 3, []);
        var provider = new FakeContainerRuntimeProvider(statusResult: new ContainerRuntimeStatusResult(true, null, [container]));
        var f = await CreateFixtureAsync(runtimeProvider: provider);
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
    public async Task RestartAsync_WithoutControlPermission_ThrowsForbidden_AndNeverInvokesProvider()
    {
        var provider = new FakeContainerRuntimeProvider(operationResult: new ContainerRuntimeOperationResult(true, true, "ok"));
        var f = await CreateFixtureAsync(runtimeProvider: provider);
        f.CurrentUser.UserId = f.ViewUserId; // holds View but not Control

        await Assert.ThrowsAsync<ForbiddenException>(() => f.Sut.RestartAsync(f.App.Id, f.DevEnv.Id));
        Assert.Equal(0, provider.OperationInvocationCount);
    }

    [Fact]
    public async Task RestartAsync_WhenReachableAndSucceeds_ReturnsSuccess_AndRecordsSuccessAudit_AndPassesTargetServer()
    {
        var provider = new FakeContainerRuntimeProvider(operationResult: new ContainerRuntimeOperationResult(true, true, "Restarting sample-web-1..."));
        var f = await CreateFixtureAsync(runtimeProvider: provider);
        f.CurrentUser.UserId = f.ControlUserId;

        var result = await f.Sut.RestartAsync(f.App.Id, f.DevEnv.Id);

        Assert.True(result.Success);
        Assert.Equal(f.TargetServer.Id, provider.OperationsInvoked[0].Server.Id);
        Assert.Equal(ComposeOperation.Restart, provider.OperationsInvoked[0].Operation);
        var auditEntry = await f.Db.AuditLogs.SingleAsync(a => a.Action == "container.restart");
        Assert.Equal(AuditResult.Success, auditEntry.Result);
        Assert.Contains("Sample", auditEntry.Details);
        Assert.Contains("server-1", auditEntry.Details);
    }

    [Fact]
    public async Task StartAsync_InvokesRestartOperation()
    {
        var provider = new FakeContainerRuntimeProvider(operationResult: new ContainerRuntimeOperationResult(true, true, "ok"));
        var f = await CreateFixtureAsync(runtimeProvider: provider);
        f.CurrentUser.UserId = f.ControlUserId;

        await f.Sut.StartAsync(f.App.Id, f.DevEnv.Id);

        Assert.Equal(ComposeOperation.Start, provider.OperationsInvoked[0].Operation);
    }

    [Fact]
    public async Task StopAsync_InvokesStopOperation()
    {
        var provider = new FakeContainerRuntimeProvider(operationResult: new ContainerRuntimeOperationResult(true, true, "ok"));
        var f = await CreateFixtureAsync(runtimeProvider: provider);
        f.CurrentUser.UserId = f.ControlUserId;

        await f.Sut.StopAsync(f.App.Id, f.DevEnv.Id);

        Assert.Equal(ComposeOperation.Stop, provider.OperationsInvoked[0].Operation);
    }

    [Fact]
    public async Task RestartAsync_WhenReachableButComposeFails_ReturnsFailureResult_AndRecordsFailureAudit_WithoutThrowing()
    {
        var provider = new FakeContainerRuntimeProvider(operationResult: new ContainerRuntimeOperationResult(true, false, "exit 1: cannot connect to the Docker daemon"));
        var f = await CreateFixtureAsync(runtimeProvider: provider);
        f.CurrentUser.UserId = f.ControlUserId;

        var result = await f.Sut.RestartAsync(f.App.Id, f.DevEnv.Id);

        Assert.False(result.Success);
        Assert.Contains("Docker daemon", result.Message);
        var auditEntry = await f.Db.AuditLogs.SingleAsync(a => a.Action == "container.restart");
        Assert.Equal(AuditResult.Failure, auditEntry.Result);
    }

    [Fact]
    public async Task RestartAsync_SanitizesSecretsFromResultMessageBeforeAuditing()
    {
        var provider = new FakeContainerRuntimeProvider(operationResult: new ContainerRuntimeOperationResult(true, true, "DB_PASSWORD=hunter2 restart complete"));
        var f = await CreateFixtureAsync(runtimeProvider: provider);
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
    public async Task RecreateWithVolumesAsync_WithoutConfirmFlag_ThrowsValidation_AndNeverInvokesProvider()
    {
        var provider = new FakeContainerRuntimeProvider(operationResult: new ContainerRuntimeOperationResult(true, true, "ok"));
        var f = await CreateFixtureAsync(runtimeProvider: provider, useDownWithVolumesOnDeploy: true);
        f.CurrentUser.UserId = f.RecreateUserId;

        await Assert.ThrowsAsync<ValidationException>(() => f.Sut.RecreateWithVolumesAsync(f.App.Id, f.DevEnv.Id, new RecreateWithVolumesRequest(false)));
        Assert.Equal(0, provider.OperationInvocationCount);
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
    public async Task RecreateWithVolumesAsync_WhenReachableWithConfirmAndExplicitOptIn_Succeeds_AndAuditsRequestedAndSucceeded()
    {
        var provider = new FakeContainerRuntimeProvider(operationResult: new ContainerRuntimeOperationResult(true, true, "ok"));
        var f = await CreateFixtureAsync(runtimeProvider: provider, useDownWithVolumesOnDeploy: true);
        f.CurrentUser.UserId = f.RecreateUserId;

        var result = await f.Sut.RecreateWithVolumesAsync(f.App.Id, f.DevEnv.Id, new RecreateWithVolumesRequest(true));

        Assert.True(result.Success);
        Assert.Equal([ComposeOperation.DownWithVolumes, ComposeOperation.Up], provider.OperationsInvoked.Select(o => o.Operation).ToList());

        var requested = await f.Db.AuditLogs.SingleAsync(a => a.Action == "container.recreate.requested");
        Assert.Contains("destroys volumes", requested.Details);
        var succeeded = await f.Db.AuditLogs.SingleAsync(a => a.Action == "container.recreate.succeeded");
        Assert.Equal(AuditResult.Success, succeeded.Result);
    }

    [Fact]
    public async Task RecreateWithVolumesAsync_ProceedsToUpEvenWhenDownFails_MatchingDeployExecutorTolerance()
    {
        var provider = new FakeContainerRuntimeProvider(perOperationResult: op =>
            op == ComposeOperation.DownWithVolumes
                ? new ContainerRuntimeOperationResult(true, false, "down failed")
                : new ContainerRuntimeOperationResult(true, true, "up succeeded"));
        var f = await CreateFixtureAsync(runtimeProvider: provider, useDownWithVolumesOnDeploy: true);
        f.CurrentUser.UserId = f.RecreateUserId;

        var result = await f.Sut.RecreateWithVolumesAsync(f.App.Id, f.DevEnv.Id, new RecreateWithVolumesRequest(true));

        Assert.True(result.Success);
        Assert.Equal([ComposeOperation.DownWithVolumes, ComposeOperation.Up], provider.OperationsInvoked.Select(o => o.Operation).ToList());
    }

    [Fact]
    public async Task RecreateWithVolumesAsync_WhenUpFails_ReturnsFailure_AndAuditsFailed()
    {
        var provider = new FakeContainerRuntimeProvider(perOperationResult: op =>
            op == ComposeOperation.DownWithVolumes
                ? new ContainerRuntimeOperationResult(true, true, "down succeeded")
                : new ContainerRuntimeOperationResult(true, false, "up failed"));
        var f = await CreateFixtureAsync(runtimeProvider: provider, useDownWithVolumesOnDeploy: true);
        f.CurrentUser.UserId = f.RecreateUserId;

        var result = await f.Sut.RecreateWithVolumesAsync(f.App.Id, f.DevEnv.Id, new RecreateWithVolumesRequest(true));

        Assert.False(result.Success);
        var failed = await f.Db.AuditLogs.SingleAsync(a => a.Action == "container.recreate.failed");
        Assert.Equal(AuditResult.Failure, failed.Result);
    }

    [Fact]
    public async Task RecreateWithVolumesAsync_WhenTargetServerUnreachable_ReturnsFailure_AndAuditsFailed_WithoutAttemptingUp()
    {
        var provider = new FakeContainerRuntimeProvider(operationResult: new ContainerRuntimeOperationResult(false, false, "not configured"));
        var f = await CreateFixtureAsync(runtimeProvider: provider, useDownWithVolumesOnDeploy: true);
        f.CurrentUser.UserId = f.RecreateUserId;

        var result = await f.Sut.RecreateWithVolumesAsync(f.App.Id, f.DevEnv.Id, new RecreateWithVolumesRequest(true));

        Assert.False(result.Success);
        Assert.Single(provider.OperationsInvoked); // only DownWithVolumes attempted, not Up
        var failed = await f.Db.AuditLogs.SingleAsync(a => a.Action == "container.recreate.failed");
        Assert.Equal(AuditResult.Failure, failed.Result);
    }

    // ---------------------------------------------------------------------- fakes

    private sealed class FakeHealthCheckProbe(bool passed, string detail = "ok") : IHealthCheckProbe
    {
        public Task<HealthCheckResult> ProbeAsync(
            HealthCheckType type, string? endpoint, int timeoutSeconds, CancellationToken cancellationToken = default) =>
            Task.FromResult(new HealthCheckResult(passed, detail));
    }

    private sealed class FakeContainerRuntimeProvider : IContainerRuntimeProvider
    {
        private readonly ContainerRuntimeStatusResult _statusResult;
        private readonly Func<ComposeOperation, ContainerRuntimeOperationResult> _operationResultFor;

        public List<(TargetServer Server, ComposeOperation Operation)> OperationsInvoked { get; } = [];
        public int OperationInvocationCount => OperationsInvoked.Count;
        public TargetServer? LastGetStatusTargetServer { get; private set; }

        public FakeContainerRuntimeProvider(
            ContainerRuntimeStatusResult? statusResult = null,
            ContainerRuntimeOperationResult? operationResult = null,
            Func<ComposeOperation, ContainerRuntimeOperationResult>? perOperationResult = null)
        {
            _statusResult = statusResult ?? new ContainerRuntimeStatusResult(false, "No remote execution mechanism is configured for target server.", []);
            _operationResultFor = perOperationResult
                ?? (_ => operationResult ?? new ContainerRuntimeOperationResult(false, false, "No remote execution mechanism is configured for target server."));
        }

        public Task<ContainerRuntimeStatusResult> GetStatusAsync(
            TargetServer targetServer, string workingDirectory, string composeFilePath, string? projectName, CancellationToken cancellationToken = default)
        {
            LastGetStatusTargetServer = targetServer;
            return Task.FromResult(_statusResult);
        }

        public Task<ContainerRuntimeOperationResult> RunOperationAsync(
            TargetServer targetServer, string workingDirectory, string composeFilePath, string? projectName, ComposeOperation operation,
            CancellationToken cancellationToken = default)
        {
            OperationsInvoked.Add((targetServer, operation));
            return Task.FromResult(_operationResultFor(operation));
        }
    }
}
