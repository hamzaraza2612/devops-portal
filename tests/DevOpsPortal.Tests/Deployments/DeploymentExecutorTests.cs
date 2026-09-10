using DevOpsPortal.Application.Abstractions;
using DevOpsPortal.Application.Services;
using DevOpsPortal.Domain.Constants;
using DevOpsPortal.Domain.Entities;
using DevOpsPortal.Domain.Enums;
using DevOpsPortal.Infrastructure.Persistence;
using DevOpsPortal.Tests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DevOpsPortal.Tests.Deployments;

public class DeploymentExecutorTests
{
    private static async Task<(DeploymentExecutor Sut, AppDbContext Db, Deployment Deployment)> CreateSutAsync(
        IComposeCommandExecutor composeExecutor, IHealthCheckProbe healthProbe)
    {
        var db = TestDb.CreateInMemory();
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
        var sut = new DeploymentExecutor(db, composeExecutor, healthProbe, audit, NullLogger<DeploymentExecutor>.Instance);
        return (sut, db, deployment);
    }

    [Fact]
    public async Task ExecuteAsync_WhenComposeUpSucceedsAndHealthPasses_MarksSucceeded()
    {
        var (sut, db, deployment) = await CreateSutAsync(new FakeComposeCommandExecutor(true, true), new FakeHealthCheckProbe(true));

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
        var (sut, db, deployment) = await CreateSutAsync(new FakeComposeCommandExecutor(true, false), new FakeHealthCheckProbe(true));

        await sut.ExecuteAsync(deployment.Id, CancellationToken.None);

        var updated = await db.Deployments.FindAsync(deployment.Id);
        Assert.Equal(DeploymentStatus.Failed, updated!.Status);
        Assert.NotNull(updated.FailureReason);
    }

    [Fact]
    public async Task ExecuteAsync_WhenHealthCheckFails_MarksFailedNotSucceeded()
    {
        var (sut, db, deployment) = await CreateSutAsync(new FakeComposeCommandExecutor(true, true), new FakeHealthCheckProbe(false));

        await sut.ExecuteAsync(deployment.Id, CancellationToken.None);

        var updated = await db.Deployments.FindAsync(deployment.Id);
        Assert.Equal(DeploymentStatus.Failed, updated!.Status);
        Assert.False(updated.HealthCheckPassed);
        Assert.Contains("Health check failed", updated.FailureReason);
    }

    [Fact]
    public async Task ExecuteAsync_SanitizesSecretsBeforePersistingLogs()
    {
        var composeExecutor = new FakeComposeCommandExecutor(true, true, upStdErr: "DB_PASSWORD=hunter2 leaked in output");
        var (sut, db, deployment) = await CreateSutAsync(composeExecutor, new FakeHealthCheckProbe(true));

        await sut.ExecuteAsync(deployment.Id, CancellationToken.None);

        var logs = await db.DeploymentLogEntries.Where(l => l.DeploymentId == deployment.Id).ToListAsync();
        Assert.NotEmpty(logs);
        Assert.DoesNotContain(logs, l => l.Message.Contains("hunter2"));
    }

    [Fact]
    public async Task ExecuteAsync_ProducesSequentialLogEntries()
    {
        var (sut, db, deployment) = await CreateSutAsync(new FakeComposeCommandExecutor(true, true), new FakeHealthCheckProbe(true));

        await sut.ExecuteAsync(deployment.Id, CancellationToken.None);

        var logs = await db.DeploymentLogEntries.Where(l => l.DeploymentId == deployment.Id).OrderBy(l => l.Sequence).ToListAsync();
        Assert.True(logs.Count >= 3);
        Assert.Equal(Enumerable.Range(1, logs.Count), logs.Select(l => l.Sequence));
    }

    [Fact]
    public async Task ExecuteAsync_AlreadyRunningDeployment_IsSkipped()
    {
        var (sut, db, deployment) = await CreateSutAsync(new FakeComposeCommandExecutor(true, true), new FakeHealthCheckProbe(true));
        deployment.Status = DeploymentStatus.Running;
        await db.SaveChangesAsync();

        await sut.ExecuteAsync(deployment.Id, CancellationToken.None);

        var updated = await db.Deployments.FindAsync(deployment.Id);
        Assert.Equal(DeploymentStatus.Running, updated!.Status);
    }

    private sealed class FakeComposeCommandExecutor(bool downSucceeds, bool upSucceeds, string? upStdErr = null) : IComposeCommandExecutor
    {
        public Task<ComposeCommandResult> RunAsync(ComposeCommandRequest request, CancellationToken cancellationToken = default)
        {
            var success = request.Operation == ComposeOperation.Up ? upSucceeds : downSucceeds;
            var stderr = request.Operation == ComposeOperation.Up ? upStdErr ?? string.Empty : string.Empty;
            return Task.FromResult(new ComposeCommandResult(success, success ? 0 : 1, "stdout", success ? stderr : "compose failed" + stderr));
        }
    }

    private sealed class FakeHealthCheckProbe(bool passed) : IHealthCheckProbe
    {
        public Task<HealthCheckResult> ProbeAsync(
            HealthCheckType type, string? endpoint, int timeoutSeconds, CancellationToken cancellationToken = default) =>
            Task.FromResult(new HealthCheckResult(passed, passed ? "ok" : "health check failed reason"));
    }
}
