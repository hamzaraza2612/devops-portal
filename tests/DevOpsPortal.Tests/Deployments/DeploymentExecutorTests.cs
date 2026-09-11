using DevOpsPortal.Application.Abstractions;
using DevOpsPortal.Application.Dtos.Secrets;
using DevOpsPortal.Application.Exceptions;
using DevOpsPortal.Application.Services;
using DevOpsPortal.Domain.Constants;
using DevOpsPortal.Domain.Entities;
using DevOpsPortal.Domain.Enums;
using DevOpsPortal.Infrastructure.Persistence;
using DevOpsPortal.Tests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Primitives;
using Xunit;

namespace DevOpsPortal.Tests.Deployments;

public class DeploymentExecutorTests
{
    private static async Task<(DeploymentExecutor Sut, AppDbContext Db, Deployment Deployment, FakeNotificationService Notifications)> CreateSutAsync(
        IRemoteExecutionProvider remoteExecutionProvider, IHealthCheckProbe healthProbe,
        ISecretReferenceService? secretReferenceService = null, FakeNotificationService? notificationService = null,
        IConfiguration? configuration = null)
    {
        var db = TestDb.CreateInMemory();
        await TestDb.SeedEnvironmentDefinitionsAsync(db);

        var app = new ManagedApplication { Name = "Sample", Slug = "sample", DeploymentMode = DeploymentMode.LegacyFilesystem };
        db.Applications.Add(app);

        // Hostname/SshUsername/SshCredentialStoreKey are only here so
        // IRemoteExecutionProvider.IsConfigured(appEnv.TargetServer) is true —
        // the fakes below never actually open an SSH connection.
        var server = new TargetServer
        {
            Name = "server-1", IsActive = true, Hostname = "10.0.0.1", SshUsername = "deploy", SshCredentialStoreKey = "fake-key",
        };
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
            PublishSubPath = "publish",
            BackupSubPath = "Backups",
            ComposeFilePath = "docker-compose.yml",
            IsActive = true,
            HealthCheckType = HealthCheckType.None,
        };
        db.ApplicationEnvironments.Add(appEnv);
        await db.SaveChangesAsync();

        var deployment = new Deployment
        {
            ApplicationId = app.Id,
            EnvironmentDefinitionId = devEnv.Id,
            ApplicationEnvironmentId = appEnv.Id,
            CommitSha = "abc1234",
            Status = DeploymentStatus.Pending,
            RequestedByUserId = Guid.NewGuid(),
        };
        db.Deployments.Add(deployment);
        await db.SaveChangesAsync();

        var audit = new AuditService(db, new FakeCurrentUserService());
        var notifications = notificationService ?? new FakeNotificationService();
        var sut = new DeploymentExecutor(
            db, remoteExecutionProvider, healthProbe, audit, secretReferenceService ?? new FakeSecretReferenceService(), notifications,
            configuration ?? new FakeConfiguration(), NullLogger<DeploymentExecutor>.Instance);
        return (sut, db, deployment, notifications);
    }

    [Fact]
    public async Task ExecuteAsync_WhenComposeUpSucceedsAndHealthPasses_MarksSucceeded()
    {
        var (sut, db, deployment, _) = await CreateSutAsync(new FakeRemoteExecutionProvider(true, true), new FakeHealthCheckProbe(true));

        await sut.ExecuteAsync(deployment.Id, CancellationToken.None);

        var updated = await db.Deployments.FindAsync(deployment.Id);
        Assert.Equal(DeploymentStatus.Succeeded, updated!.Status);
        Assert.NotNull(updated.StartedAt);
        Assert.NotNull(updated.CompletedAt);
        Assert.True(updated.HealthCheckPassed);
    }

    [Fact]
    public async Task ExecuteAsync_WhenComposeUpFails_MarksFailed()
    {
        var (sut, db, deployment, _) = await CreateSutAsync(new FakeRemoteExecutionProvider(true, false), new FakeHealthCheckProbe(true));

        await sut.ExecuteAsync(deployment.Id, CancellationToken.None);

        var updated = await db.Deployments.FindAsync(deployment.Id);
        Assert.Equal(DeploymentStatus.Failed, updated!.Status);
        Assert.NotNull(updated.FailureReason);
    }

    [Fact]
    public async Task ExecuteAsync_WhenComposeCommandHangsPastTheConfiguredTimeout_MarksFailedWithTimeoutReason()
    {
        var (sut, db, deployment, _) = await CreateSutAsync(
            new HangingRemoteExecutionProvider(), new FakeHealthCheckProbe(true),
            configuration: new FakeConfiguration(executionTimeoutMinutes: "0.0005")); // ~30ms

        await sut.ExecuteAsync(deployment.Id, CancellationToken.None);

        var updated = await db.Deployments.FindAsync(deployment.Id);
        Assert.Equal(DeploymentStatus.Failed, updated!.Status);
        Assert.Contains("timed out", updated.FailureReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExecuteAsync_WhenHealthCheckFails_MarksFailedNotSucceeded()
    {
        var (sut, db, deployment, _) = await CreateSutAsync(new FakeRemoteExecutionProvider(true, true), new FakeHealthCheckProbe(false));

        await sut.ExecuteAsync(deployment.Id, CancellationToken.None);

        var updated = await db.Deployments.FindAsync(deployment.Id);
        Assert.Equal(DeploymentStatus.Failed, updated!.Status);
        Assert.False(updated.HealthCheckPassed);
        Assert.Contains("Health check failed", updated.FailureReason);
    }

    [Fact]
    public async Task ExecuteAsync_SanitizesSecretsBeforePersistingLogs()
    {
        var composeExecutor = new FakeRemoteExecutionProvider(true, true, upStdErr: "DB_PASSWORD=hunter2 leaked in output");
        var (sut, db, deployment, _) = await CreateSutAsync(composeExecutor, new FakeHealthCheckProbe(true));

        await sut.ExecuteAsync(deployment.Id, CancellationToken.None);

        var logs = await db.DeploymentLogEntries.Where(l => l.DeploymentId == deployment.Id).ToListAsync();
        Assert.NotEmpty(logs);
        Assert.DoesNotContain(logs, l => l.Message.Contains("hunter2"));
    }

    [Fact]
    public async Task ExecuteAsync_ProducesSequentialLogEntries()
    {
        var (sut, db, deployment, _) = await CreateSutAsync(new FakeRemoteExecutionProvider(true, true), new FakeHealthCheckProbe(true));

        await sut.ExecuteAsync(deployment.Id, CancellationToken.None);

        var logs = await db.DeploymentLogEntries.Where(l => l.DeploymentId == deployment.Id).OrderBy(l => l.Sequence).ToListAsync();
        Assert.True(logs.Count >= 3);
        Assert.Equal(Enumerable.Range(1, logs.Count), logs.Select(l => l.Sequence));
    }

    [Fact]
    public async Task ExecuteAsync_PassesResolvedSecretsAsProcessEnvironmentVariables_NeverAsArguments()
    {
        var composeExecutor = new FakeRemoteExecutionProvider(true, true);
        var secretService = new FakeSecretReferenceService(new Dictionary<string, string> { ["DB_PASSWORD"] = "hunter2" });
        var (sut, db, deployment, _) = await CreateSutAsync(composeExecutor, new FakeHealthCheckProbe(true), secretService);

        await sut.ExecuteAsync(deployment.Id, CancellationToken.None);

        var updated = await db.Deployments.FindAsync(deployment.Id);
        Assert.Equal(DeploymentStatus.Succeeded, updated!.Status);
        Assert.Contains(composeExecutor.Requests, r => r.EnvironmentVariables != null && r.EnvironmentVariables["DB_PASSWORD"] == "hunter2");

        var logs = await db.DeploymentLogEntries.Where(l => l.DeploymentId == deployment.Id).ToListAsync();
        Assert.DoesNotContain(logs, l => l.Message.Contains("hunter2"));
        Assert.Contains(logs, l => l.Message.Contains("DB_PASSWORD") && !l.Message.Contains("hunter2"));
    }

    [Fact]
    public async Task ExecuteAsync_RedactsResolvedSecretValueFromComposeOutput_EvenWithoutKeyValueShape()
    {
        var composeExecutor = new FakeRemoteExecutionProvider(true, true, upStdErr: "connecting with password hunter2 to db host");
        var secretService = new FakeSecretReferenceService(new Dictionary<string, string> { ["DB_PASSWORD"] = "hunter2" });
        var (sut, db, deployment, _) = await CreateSutAsync(composeExecutor, new FakeHealthCheckProbe(true), secretService);

        await sut.ExecuteAsync(deployment.Id, CancellationToken.None);

        var logs = await db.DeploymentLogEntries.Where(l => l.DeploymentId == deployment.Id).ToListAsync();
        Assert.DoesNotContain(logs, l => l.Message.Contains("hunter2"));
        Assert.Contains(logs, l => l.Message.Contains("REDACTED"));
    }

    [Fact]
    public async Task ExecuteAsync_WhenSecretResolutionFails_MarksFailed_NeverCallsCompose()
    {
        var composeExecutor = new FakeRemoteExecutionProvider(true, true);
        var secretService = new FakeSecretReferenceService(resolveError: "Failed to resolve secret 'db-password' for this deployment.");
        var (sut, db, deployment, _) = await CreateSutAsync(composeExecutor, new FakeHealthCheckProbe(true), secretService);

        await sut.ExecuteAsync(deployment.Id, CancellationToken.None);

        var updated = await db.Deployments.FindAsync(deployment.Id);
        Assert.Equal(DeploymentStatus.Failed, updated!.Status);
        Assert.Contains("db-password", updated.FailureReason);
        Assert.Empty(composeExecutor.Requests);
    }

    [Fact]
    public async Task ExecuteAsync_AlreadyRunningDeployment_IsSkipped()
    {
        var (sut, db, deployment, _) = await CreateSutAsync(new FakeRemoteExecutionProvider(true, true), new FakeHealthCheckProbe(true));
        deployment.Status = DeploymentStatus.Running;
        await db.SaveChangesAsync();

        await sut.ExecuteAsync(deployment.Id, CancellationToken.None);

        var updated = await db.Deployments.FindAsync(deployment.Id);
        Assert.Equal(DeploymentStatus.Running, updated!.Status);
    }

    [Fact]
    public async Task ExecuteAsync_WhenSucceeds_NotifiesStartedThenOutcome()
    {
        var (sut, db, deployment, notifications) = await CreateSutAsync(new FakeRemoteExecutionProvider(true, true), new FakeHealthCheckProbe(true));

        await sut.ExecuteAsync(deployment.Id, CancellationToken.None);

        Assert.Equal(1, notifications.StartedCallCount);
        Assert.Equal(1, notifications.OutcomeCallCount);
        Assert.Equal(DeploymentStatus.Succeeded, notifications.LastOutcomeStatus);
    }

    [Fact]
    public async Task ExecuteAsync_WhenFails_StillNotifiesStartedAndOutcome()
    {
        var (sut, db, deployment, notifications) = await CreateSutAsync(new FakeRemoteExecutionProvider(true, false), new FakeHealthCheckProbe(true));

        await sut.ExecuteAsync(deployment.Id, CancellationToken.None);

        Assert.Equal(1, notifications.StartedCallCount);
        Assert.Equal(1, notifications.OutcomeCallCount);
        Assert.Equal(DeploymentStatus.Failed, notifications.LastOutcomeStatus);
    }

    [Fact]
    public async Task ExecuteAsync_WhenNotificationServiceThrows_DeploymentOutcomeIsUnaffected()
    {
        var notifications = new FakeNotificationService { ThrowOnStarted = true, ThrowOnOutcome = true };
        var (sut, db, deployment, _) = await CreateSutAsync(
            new FakeRemoteExecutionProvider(true, true), new FakeHealthCheckProbe(true), notificationService: notifications);

        await sut.ExecuteAsync(deployment.Id, CancellationToken.None);

        var updated = await db.Deployments.FindAsync(deployment.Id);
        Assert.Equal(DeploymentStatus.Succeeded, updated!.Status);
    }

    private sealed class FakeRemoteExecutionProvider(bool downSucceeds, bool upSucceeds, string? upStdErr = null) : IRemoteExecutionProvider
    {
        public List<ComposeCommandRequest> Requests { get; } = [];

        public bool IsConfigured(TargetServer targetServer) => true;

        public Task<ComposeCommandResult> RunComposeAsync(TargetServer targetServer, ComposeCommandRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            var success = request.Operation == ComposeOperation.Up ? upSucceeds : downSucceeds;
            var stderr = request.Operation == ComposeOperation.Up ? upStdErr ?? string.Empty : string.Empty;
            return Task.FromResult(new ComposeCommandResult(success, success ? 0 : 1, "stdout", success ? stderr : "compose failed" + stderr));
        }

        public Task<RemoteContainerInspectResult> InspectContainerAsync(TargetServer targetServer, string containerName, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<RemoteContainerLogsResult> GetContainerLogsAsync(TargetServer targetServer, string containerName, int tailLines, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<RemoteContainerStatsResult> GetContainerStatsAsync(TargetServer targetServer, string containerName, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<RemoteConnectionTestResult> TestConnectionAsync(TargetServer targetServer, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    /// <summary>Never completes on its own — only responds to cancellation. Used to
    /// simulate a stuck `docker compose up` for the execution-timeout test.</summary>
    private sealed class HangingRemoteExecutionProvider : IRemoteExecutionProvider
    {
        public bool IsConfigured(TargetServer targetServer) => true;

        public async Task<ComposeCommandResult> RunComposeAsync(TargetServer targetServer, ComposeCommandRequest request, CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            throw new InvalidOperationException("Unreachable — Task.Delay(Infinite) only returns via cancellation.");
        }

        public Task<RemoteContainerInspectResult> InspectContainerAsync(TargetServer targetServer, string containerName, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<RemoteContainerLogsResult> GetContainerLogsAsync(TargetServer targetServer, string containerName, int tailLines, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<RemoteContainerStatsResult> GetContainerStatsAsync(TargetServer targetServer, string containerName, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<RemoteConnectionTestResult> TestConnectionAsync(TargetServer targetServer, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class FakeHealthCheckProbe(bool passed) : IHealthCheckProbe
    {
        public Task<HealthCheckResult> ProbeAsync(
            HealthCheckType type, string? endpoint, int timeoutSeconds, CancellationToken cancellationToken = default) =>
            Task.FromResult(new HealthCheckResult(passed, passed ? "ok" : "health check failed reason"));
    }

    private sealed class FakeSecretReferenceService(
        IReadOnlyDictionary<string, string>? secrets = null, string? resolveError = null) : ISecretReferenceService
    {
        public Task<IReadOnlyList<SecretReferenceDto>> ListAsync(
            Guid? applicationId, Guid? environmentDefinitionId, SecretCategory? category, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<SecretReferenceDto> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<SecretReferenceDto> CreateAsync(CreateSecretReferenceRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<SecretReferenceDto> UpdateAsync(Guid id, UpdateSecretReferenceRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<RevealedSecretDto> RevealAsync(Guid id, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyDictionary<string, string>> ResolveForDeploymentAsync(
            Guid applicationId, Guid environmentDefinitionId, Guid actorUserId, string? actorUsername, CancellationToken cancellationToken = default)
        {
            if (resolveError is not null)
                throw new DeploymentExecutionException(resolveError);

            return Task.FromResult(secrets ?? new Dictionary<string, string>());
        }
    }

    private sealed class FakeNotificationService : INotificationService
    {
        public int StartedCallCount { get; private set; }
        public int OutcomeCallCount { get; private set; }
        public DeploymentStatus? LastOutcomeStatus { get; private set; }
        public bool ThrowOnStarted { get; set; }
        public bool ThrowOnOutcome { get; set; }

        public Task NotifyPromotionApprovalRequestedAsync(
            PromotionRequest promotion, string permissionCode, string rawApprovalToken, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task NotifyProductionApprovalRequestedAsync(
            PromotionRequest promotion, ProductionApproval approval, string rawApprovalToken, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task NotifyDeploymentStartedAsync(Deployment deployment, CancellationToken cancellationToken = default)
        {
            StartedCallCount++;
            if (ThrowOnStarted)
                throw new InvalidOperationException("Simulated notification failure.");
            return Task.CompletedTask;
        }

        public Task NotifyDeploymentOutcomeAsync(Deployment deployment, CancellationToken cancellationToken = default)
        {
            OutcomeCallCount++;
            LastOutcomeStatus = deployment.Status;
            if (ThrowOnOutcome)
                throw new InvalidOperationException("Simulated notification failure.");
            return Task.CompletedTask;
        }
    }

    private sealed class FakeConfiguration(string? executionTimeoutMinutes = null) : IConfiguration
    {
        public string? this[string key]
        {
            get => key == "Deployment:ExecutionTimeoutMinutes" ? executionTimeoutMinutes : null;
            set { }
        }

        public IEnumerable<IConfigurationSection> GetChildren() => [];
        public IChangeToken GetReloadToken() => throw new NotSupportedException();
        public IConfigurationSection GetSection(string key) => throw new NotSupportedException();
    }
}
