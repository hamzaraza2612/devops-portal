using DevOpsPortal.Application.Abstractions;
using DevOpsPortal.Application.Dtos.Infrastructure;
using DevOpsPortal.Application.Exceptions;
using DevOpsPortal.Application.Services;
using DevOpsPortal.Domain.Entities;
using DevOpsPortal.Tests.Common;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DevOpsPortal.Tests.Services;

/// <summary>
/// Phase 13c "real server/Docker discovery" — this service is the boundary
/// most exposed to a caller-supplied containerId, so most of these tests are
/// about the two honesty guarantees the master requirements insist on: never
/// convert a real failure (unassigned server, SSH down, Docker down) into
/// what looks like "zero containers", and never trust a browser-supplied
/// container id without re-checking it against a fresh discovery pass.
/// </summary>
public class EnvironmentInfrastructureServiceTests
{
    [Fact]
    public async Task GetInfrastructureAsync_WithNoPrimaryTargetServer_ReportsNotConfiguredWithoutAttemptingConnection()
    {
        var db = TestDb.CreateInMemory();
        await TestDb.SeedEnvironmentDefinitionsAsync(db);
        var userId = await TestDb.CreateAdminAsync(db, "admin");
        var envId = await db.EnvironmentDefinitions.Where(e => e.Name == "DEV").Select(e => e.Id).FirstAsync();
        var remote = new FakeInfraRemoteExecutionProvider();
        var currentUser = new FakeCurrentUserServiceWithId(userId);
        var sut = new EnvironmentInfrastructureService(db, currentUser, new AuditService(db, currentUser), remote);

        var result = await sut.GetInfrastructureAsync(envId);

        Assert.False(result.Server.IsConfigured);
        Assert.False(result.Server.SshConnected);
        Assert.Empty(result.Containers);
        Assert.NotNull(result.Server.ErrorMessage);
        Assert.Equal(0, remote.DiscoverCallCount);
    }

    [Fact]
    public async Task GetInfrastructureAsync_WhenSshUnreachable_ReportsErrorAndNeverAttemptsDiscovery()
    {
        var db = TestDb.CreateInMemory();
        var (userId, serverId) = await SeedAdminWithServerAsync(db);
        var remote = new FakeInfraRemoteExecutionProvider
        {
            ConnectionResult = new RemoteConnectionTestResult(false, null, null, false, null, false, null, null, null, null, "SSH connection failed: timed out"),
        };
        var currentUser = new FakeCurrentUserServiceWithId(userId);
        var sut = new EnvironmentInfrastructureService(db, currentUser, new AuditService(db, currentUser), remote);
        var envId = await AssignServerToDevAsync(db, serverId);

        var result = await sut.GetInfrastructureAsync(envId);

        Assert.True(result.Server.IsConfigured);
        Assert.False(result.Server.SshConnected);
        Assert.Contains("SSH connection failed", result.Server.ErrorMessage);
        Assert.Empty(result.Containers);
        Assert.Equal(0, remote.SnapshotIncludedDiscoveryCount);
    }

    [Fact]
    public async Task GetInfrastructureAsync_WhenDockerUnavailable_ReportsErrorAndNeverAttemptsDiscovery()
    {
        var db = TestDb.CreateInMemory();
        var (userId, serverId) = await SeedAdminWithServerAsync(db);
        var remote = new FakeInfraRemoteExecutionProvider
        {
            ConnectionResult = new RemoteConnectionTestResult(true, "root", "Linux", false, null, true, "2.20", "up", "mem", "disk", "Docker is not available"),
        };
        var currentUser = new FakeCurrentUserServiceWithId(userId);
        var sut = new EnvironmentInfrastructureService(db, currentUser, new AuditService(db, currentUser), remote);
        var envId = await AssignServerToDevAsync(db, serverId);

        var result = await sut.GetInfrastructureAsync(envId);

        Assert.True(result.Server.SshConnected);
        Assert.False(result.Server.DockerAvailable);
        Assert.Empty(result.Containers);
        Assert.Equal(0, remote.SnapshotIncludedDiscoveryCount);
    }

    [Fact]
    public async Task GetInfrastructureAsync_MapsConfiguredContainerByName_AndLeavesOthersUnregistered()
    {
        var db = TestDb.CreateInMemory();
        var (userId, serverId) = await SeedAdminWithServerAsync(db);
        var envId = await AssignServerToDevAsync(db, serverId);

        var app = new ManagedApplication { Name = "DmsApi", Slug = "dmsapi" };
        db.Applications.Add(app);
        db.ApplicationEnvironments.Add(new ApplicationEnvironment
        {
            ApplicationId = app.Id, EnvironmentDefinitionId = envId, TargetServerId = serverId,
            ContainerName = "dmsapi-web-1", PublishSubPath = "publish", BackupSubPath = "backup", ComposeFilePath = "docker-compose.yml",
        });
        await db.SaveChangesAsync();

        const string inspectJson = """
        [
          {"Id":"c1","Name":"/dmsapi-web-1","Created":"2026-01-01T00:00:00Z","State":{"Status":"running"},"RestartCount":0,"Config":{"Image":"dmsapi:1"}},
          {"Id":"c2","Name":"/some-other-container","Created":"2026-01-01T00:00:00Z","State":{"Status":"running"},"RestartCount":0,"Config":{"Image":"other:1"}}
        ]
        """;
        var remote = new FakeInfraRemoteExecutionProvider { DiscoveryResult = new RemoteContainerDiscoveryResult(true, inspectJson, "", null) };
        var currentUser = new FakeCurrentUserServiceWithId(userId);
        var sut = new EnvironmentInfrastructureService(db, currentUser, new AuditService(db, currentUser), remote);

        var result = await sut.GetInfrastructureAsync(envId);

        Assert.Equal(2, result.Containers.Count);
        var mapped = result.Containers.Single(c => c.Name == "dmsapi-web-1");
        Assert.True(mapped.IsMapped);
        Assert.Equal("DmsApi", mapped.ApplicationName);
        var unmapped = result.Containers.Single(c => c.Name == "some-other-container");
        Assert.False(unmapped.IsMapped);
        Assert.Null(unmapped.ApplicationName);
    }

    [Fact]
    public async Task GetInfrastructureAsync_MakesExactlyOneRemoteCall_NeverTestConnectionAndHostMetricsConcurrently()
    {
        // Regression test for a live crash: TestConnectionAsync and
        // GetHostMetricsAsync each resolve the target server's stored SSH
        // credential through the same scoped ISecretProvider/DbContext -
        // running them concurrently (e.g. via Task.WhenAll) throws "A second
        // operation was started on this context instance before a previous
        // operation completed" against the real EncryptedSecretProvider.
        // GetInfrastructureAsync must use the single combined
        // GetEnvironmentSnapshotAsync call and never call TestConnectionAsync/
        // GetHostMetricsAsync/DiscoverContainersAsync itself.
        var db = TestDb.CreateInMemory();
        var (userId, serverId) = await SeedAdminWithServerAsync(db);
        var envId = await AssignServerToDevAsync(db, serverId);
        var remote = new FakeInfraRemoteExecutionProvider();
        var currentUser = new FakeCurrentUserServiceWithId(userId);
        var sut = new EnvironmentInfrastructureService(db, currentUser, new AuditService(db, currentUser), remote);

        await sut.GetInfrastructureAsync(envId);

        Assert.Equal(1, remote.SnapshotCallCount);
        Assert.Equal(0, remote.TestConnectionCallCount);
        Assert.Equal(0, remote.GetHostMetricsCallCount);
        Assert.Equal(0, remote.DiscoverCallCount);
    }

    [Fact]
    public async Task GetInfrastructureAsync_UserWithoutEnvironmentAccess_ThrowsForbidden()
    {
        var db = TestDb.CreateInMemory();
        await TestDb.SeedEnvironmentDefinitionsAsync(db);
        var userId = await TestDb.CreateUserWithEnvironmentAccessAsync(db, "qa-user", "QA");
        var envId = await db.EnvironmentDefinitions.Where(e => e.Name == "DEV").Select(e => e.Id).FirstAsync();
        var remote = new FakeInfraRemoteExecutionProvider();
        var currentUser = new FakeCurrentUserServiceWithId(userId);
        var sut = new EnvironmentInfrastructureService(db, currentUser, new AuditService(db, currentUser), remote);

        await Assert.ThrowsAsync<ForbiddenException>(() => sut.GetInfrastructureAsync(envId));
    }

    [Fact]
    public async Task RunContainerActionAsync_WithUnknownContainerId_ThrowsValidationWithoutRunningAnyAction()
    {
        var db = TestDb.CreateInMemory();
        var (userId, serverId) = await SeedAdminWithServerAsync(db);
        var envId = await AssignServerToDevAsync(db, serverId);
        var remote = new FakeInfraRemoteExecutionProvider
        {
            DiscoveryResult = new RemoteContainerDiscoveryResult(true, "[]", "", null),
        };
        var currentUser = new FakeCurrentUserServiceWithId(userId);
        var sut = new EnvironmentInfrastructureService(db, currentUser, new AuditService(db, currentUser), remote);

        await Assert.ThrowsAsync<ValidationException>(() =>
            sut.RunContainerActionAsync(envId, "unknown-container-id", RemoteContainerAction.Restart));

        Assert.False(remote.ActionWasCalled);
    }

    [Fact]
    public async Task RunContainerActionAsync_WithKnownContainerId_RunsActionAndAudits()
    {
        var db = TestDb.CreateInMemory();
        var (userId, serverId) = await SeedAdminWithServerAsync(db);
        var envId = await AssignServerToDevAsync(db, serverId);
        const string inspectJson = """[{"Id":"c1","Name":"/svc","Created":"2026-01-01T00:00:00Z","State":{"Status":"running"},"RestartCount":0,"Config":{"Image":"svc:1"}}]""";
        var remote = new FakeInfraRemoteExecutionProvider
        {
            DiscoveryResult = new RemoteContainerDiscoveryResult(true, inspectJson, "", null),
            ActionResult = new RemoteContainerActionResult(true, "Container restarted.", null),
        };
        var currentUser = new FakeCurrentUserServiceWithId(userId);
        var sut = new EnvironmentInfrastructureService(db, currentUser, new AuditService(db, currentUser), remote);

        var result = await sut.RunContainerActionAsync(envId, "c1", RemoteContainerAction.Restart);

        Assert.True(result.Success);
        Assert.True(remote.ActionWasCalled);
        Assert.Equal(1, await db.AuditLogs.CountAsync());
    }

    [Fact]
    public async Task GetContainerLogsAsync_WithUnknownContainerId_ReturnsFailureWithoutFetchingLogs()
    {
        var db = TestDb.CreateInMemory();
        var (userId, serverId) = await SeedAdminWithServerAsync(db);
        var envId = await AssignServerToDevAsync(db, serverId);
        var remote = new FakeInfraRemoteExecutionProvider { DiscoveryResult = new RemoteContainerDiscoveryResult(true, "[]", "", null) };
        var currentUser = new FakeCurrentUserServiceWithId(userId);
        var sut = new EnvironmentInfrastructureService(db, currentUser, new AuditService(db, currentUser), remote);

        var result = await sut.GetContainerLogsAsync(envId, "unknown-id", 100);

        Assert.False(result.Success);
        Assert.False(remote.LogsWereFetched);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public async Task GetContainerLogsAsync_WithKnownContainerId_ReturnsSanitizedLogs()
    {
        var db = TestDb.CreateInMemory();
        var (userId, serverId) = await SeedAdminWithServerAsync(db);
        var envId = await AssignServerToDevAsync(db, serverId);
        const string inspectJson = """[{"Id":"c1","Name":"/svc","Created":"2026-01-01T00:00:00Z","State":{"Status":"running"},"RestartCount":0,"Config":{"Image":"svc:1"}}]""";
        var remote = new FakeInfraRemoteExecutionProvider
        {
            DiscoveryResult = new RemoteContainerDiscoveryResult(true, inspectJson, "", null),
            LogsResult = new RemoteContainerLogsResult(true, "plain startup log line", null),
        };
        var currentUser = new FakeCurrentUserServiceWithId(userId);
        var sut = new EnvironmentInfrastructureService(db, currentUser, new AuditService(db, currentUser), remote);

        var result = await sut.GetContainerLogsAsync(envId, "c1", 100);

        Assert.True(result.Success);
        Assert.True(remote.LogsWereFetched);
        Assert.Contains("plain startup log line", result.Logs);
    }

    [Fact]
    public void EnvironmentServerInfoDto_NeverExposesAnySshCredentialField()
    {
        // Secret non-disclosure guard: fails loudly if a future edit ever adds a
        // field whose name suggests it carries the credential itself, rather
        // than metadata about it.
        var disallowed = new[] { "credential", "password", "secret", "privatekey", "passphrase", "token" };
        foreach (var name in typeof(EnvironmentServerInfoDto).GetProperties().Select(p => p.Name.ToLowerInvariant()))
        {
            Assert.DoesNotContain(disallowed, bad => name.Contains(bad));
        }
    }

    private static async Task<(Guid UserId, Guid ServerId)> SeedAdminWithServerAsync(Infrastructure.Persistence.AppDbContext db)
    {
        await TestDb.SeedEnvironmentDefinitionsAsync(db);
        var userId = await TestDb.CreateAdminAsync(db, "admin");
        var server = new TargetServer { Name = "dev-server", Hostname = "10.0.0.5", SshUsername = "deploy", SshCredentialStoreKey = "key-1" };
        db.TargetServers.Add(server);
        await db.SaveChangesAsync();
        return (userId, server.Id);
    }

    private static async Task<Guid> AssignServerToDevAsync(Infrastructure.Persistence.AppDbContext db, Guid serverId)
    {
        var dev = await db.EnvironmentDefinitions.FirstAsync(e => e.Name == "DEV");
        dev.PrimaryTargetServerId = serverId;
        await db.SaveChangesAsync();
        return dev.Id;
    }

    private sealed class FakeCurrentUserServiceWithId(Guid userId) : ICurrentUserService
    {
        public Guid? UserId { get; } = userId;
        public string? Username => "test";
        public string? IpAddress => "127.0.0.1";
    }

    private sealed class FakeInfraRemoteExecutionProvider : IRemoteExecutionProvider
    {
        public bool Configured { get; set; } = true;
        public RemoteConnectionTestResult ConnectionResult { get; set; } =
            new(true, "root", "Linux", true, "24.0.0", true, "2.20.0", "up 1 day", "mem info", "disk info", null);
        public RemoteHostMetricsResult HostMetricsResult { get; set; } =
            new(true, "0.10 0.20 0.30", null, null, null);
        public RemoteContainerDiscoveryResult DiscoveryResult { get; set; } = new(true, "[]", "", null);
        public RemoteContainerActionResult ActionResult { get; set; } = new(true, "ok", null);
        public RemoteContainerLogsResult LogsResult { get; set; } = new(true, "log", null);

        public int DiscoverCallCount { get; private set; }
        public bool ActionWasCalled { get; private set; }
        public bool LogsWereFetched { get; private set; }
        public int SnapshotCallCount { get; private set; }
        public int SnapshotIncludedDiscoveryCount { get; private set; }
        public int TestConnectionCallCount { get; private set; }
        public int GetHostMetricsCallCount { get; private set; }

        public bool IsConfigured(TargetServer targetServer) => Configured;

        public Task<ComposeCommandResult> RunComposeAsync(TargetServer targetServer, ComposeCommandRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<RemoteContainerInspectResult> InspectContainerAsync(TargetServer targetServer, string containerName, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<RemoteContainerLogsResult> GetContainerLogsAsync(TargetServer targetServer, string containerName, int tailLines, CancellationToken cancellationToken = default)
        {
            LogsWereFetched = true;
            return Task.FromResult(LogsResult);
        }

        public Task<RemoteContainerStatsResult> GetContainerStatsAsync(TargetServer targetServer, string containerName, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<RemoteConnectionTestResult> TestConnectionAsync(TargetServer targetServer, CancellationToken cancellationToken = default)
        {
            TestConnectionCallCount++;
            return Task.FromResult(ConnectionResult);
        }

        public Task<RemoteContainerDiscoveryResult> DiscoverContainersAsync(TargetServer targetServer, CancellationToken cancellationToken = default)
        {
            DiscoverCallCount++;
            return Task.FromResult(DiscoveryResult);
        }

        public Task<RemoteContainerActionResult> RunContainerActionAsync(
            TargetServer targetServer, string containerId, RemoteContainerAction action, CancellationToken cancellationToken = default)
        {
            ActionWasCalled = true;
            return Task.FromResult(ActionResult);
        }

        public Task<RemoteHostMetricsResult> GetHostMetricsAsync(TargetServer targetServer, CancellationToken cancellationToken = default)
        {
            GetHostMetricsCallCount++;
            return Task.FromResult(HostMetricsResult);
        }

        /// <summary>Composes from ConnectionResult/HostMetricsResult/DiscoveryResult
        /// (rather than an independent field) so every existing test that sets
        /// those up still exercises the same scenario now that
        /// EnvironmentInfrastructureService calls this single combined method
        /// instead of TestConnectionAsync+GetHostMetricsAsync+DiscoverContainersAsync
        /// separately. Mirrors the real SshRemoteExecutionProvider's own rule:
        /// discovery only runs (tracked via SnapshotIncludedDiscoveryCount, kept
        /// distinct from DiscoverCallCount which tracks the separate standalone
        /// DiscoverContainersAsync method) when the connection succeeded AND
        /// Docker is available.</summary>
        public Task<RemoteEnvironmentSnapshotResult> GetEnvironmentSnapshotAsync(TargetServer targetServer, CancellationToken cancellationToken = default)
        {
            SnapshotCallCount++;
            var c = ConnectionResult;
            var m = HostMetricsResult;
            var includeDiscovery = c.SshConnected && c.DockerAvailable;
            if (includeDiscovery) SnapshotIncludedDiscoveryCount++;

            return Task.FromResult(new RemoteEnvironmentSnapshotResult(
                c.SshConnected, c.AuthenticatedUser, c.OsInfo, c.DockerAvailable, c.DockerVersion,
                c.ComposeAvailable, c.ComposeVersion, c.UptimeInfo,
                m.LoadAvgRaw, m.MemRaw, m.DiskRaw,
                includeDiscovery ? DiscoveryResult.InspectJson : string.Empty,
                includeDiscovery ? DiscoveryResult.StatsJson : string.Empty,
                c.ErrorMessage));
        }
    }
}
