using DevOpsPortal.Application.Abstractions;
using DevOpsPortal.Application.Services;
using DevOpsPortal.Domain.Constants;
using DevOpsPortal.Domain.Entities;
using DevOpsPortal.Domain.Enums;
using DevOpsPortal.Infrastructure.Persistence;
using DevOpsPortal.Tests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DevOpsPortal.Tests.Services;

public class NotificationServiceTests
{
    private sealed record Fixture(NotificationService Sut, AppDbContext Db, ManagedApplication App, EnvironmentDefinition Env, Guid ApproverUserId);

    private static async Task<Fixture> CreateFixtureAsync(params INotificationProvider[] providers)
    {
        var db = TestDb.CreateInMemory();
        await TestDb.SeedRolesAndPermissionsAsync(db);
        await TestDb.SeedEnvironmentDefinitionsAsync(db);

        var app = new ManagedApplication { Name = "Sample", Slug = "sample", DeploymentMode = DeploymentMode.LegacyFilesystem };
        db.Applications.Add(app);
        await db.SaveChangesAsync();

        var env = await db.EnvironmentDefinitions.SingleAsync(e => e.Name == EnvironmentNames.Qa);
        var approverId = await TestDb.CreateUserWithPermissionsAsync(db, "qa-approver", PermissionCodes.DeploymentsApproveQa);

        var currentUser = new FakeCurrentUserService();
        var audit = new AuditService(db, currentUser, new FakeCurrentTenantService());
        var sut = new NotificationService(db, providers, audit, new FakeConfiguration(), NullLogger<NotificationService>.Instance);

        return new Fixture(sut, db, app, env, approverId);
    }

    private static PromotionRequest BuildPromotion(Fixture f, EnvironmentDefinition fromEnv, Guid requestedByUserId) => new()
    {
        Application = f.App,
        ApplicationId = f.App.Id,
        FromEnvironmentDefinition = fromEnv,
        FromEnvironmentDefinitionId = fromEnv.Id,
        ToEnvironmentDefinition = f.Env,
        ToEnvironmentDefinitionId = f.Env.Id,
        CommitSha = "abc1234",
        RequestedByUserId = requestedByUserId,
        ApprovalTokenHash = "irrelevant-for-these-tests",
        ApprovalTokenExpiresAt = DateTimeOffset.UtcNow.AddDays(1),
    };

    [Fact]
    public async Task NotifyPromotionApprovalRequestedAsync_WithNoRecipients_DoesNotCallAnyProvider_NeverThrows()
    {
        var provider = new FakeNotificationProvider(true);
        var f = await CreateFixtureAsync(provider);
        var devEnv = await f.Db.EnvironmentDefinitions.SingleAsync(e => e.Name == EnvironmentNames.Dev);
        var promotion = BuildPromotion(f, devEnv, Guid.NewGuid());
        f.Db.PromotionRequests.Add(promotion);
        await f.Db.SaveChangesAsync();

        // deployments.approve.uat — nobody holds this in this fixture.
        await f.Sut.NotifyPromotionApprovalRequestedAsync(promotion, PermissionCodes.DeploymentsApproveUat, "raw-token", CancellationToken.None);

        Assert.Empty(provider.Sent);
        Assert.Null(promotion.NotifiedAt);
    }

    [Fact]
    public async Task NotifyPromotionApprovalRequestedAsync_BroadcastsToEveryProvider_EvenWhenOneFails()
    {
        var succeedingProvider = new FakeNotificationProvider(true);
        var failingProvider = new FakeNotificationProvider(false);
        var f = await CreateFixtureAsync(failingProvider, succeedingProvider);
        var devEnv = await f.Db.EnvironmentDefinitions.SingleAsync(e => e.Name == EnvironmentNames.Dev);
        var promotion = BuildPromotion(f, devEnv, f.ApproverUserId);
        f.Db.PromotionRequests.Add(promotion);
        await f.Db.SaveChangesAsync();

        await f.Sut.NotifyPromotionApprovalRequestedAsync(promotion, PermissionCodes.DeploymentsApproveQa, "raw-token-abc", CancellationToken.None);

        Assert.Single(failingProvider.Sent);
        Assert.Single(succeedingProvider.Sent);
        Assert.NotNull(promotion.NotifiedAt); // at least one provider succeeded
        Assert.Contains("raw-token-abc", succeedingProvider.Sent[0].Body);
    }

    [Fact]
    public async Task NotifyPromotionApprovalRequestedAsync_WhenProviderThrows_OtherProviderStillRuns_NeverThrows()
    {
        var throwingProvider = new ThrowingNotificationProvider();
        var succeedingProvider = new FakeNotificationProvider(true);
        var f = await CreateFixtureAsync(throwingProvider, succeedingProvider);
        var devEnv = await f.Db.EnvironmentDefinitions.SingleAsync(e => e.Name == EnvironmentNames.Dev);
        var promotion = BuildPromotion(f, devEnv, f.ApproverUserId);
        f.Db.PromotionRequests.Add(promotion);
        await f.Db.SaveChangesAsync();

        await f.Sut.NotifyPromotionApprovalRequestedAsync(promotion, PermissionCodes.DeploymentsApproveQa, "raw-token", CancellationToken.None);

        Assert.Single(succeedingProvider.Sent);
        Assert.NotNull(promotion.NotifiedAt);
    }

    [Fact]
    public async Task NotifyDeploymentStartedAsync_NotifiesTheRequester()
    {
        var provider = new FakeNotificationProvider(true);
        var f = await CreateFixtureAsync(provider);
        var requesterId = await TestDb.CreateUserWithPermissionsAsync(f.Db, "requester", PermissionCodes.DeploymentsDeployDev);
        var deployment = new Deployment
        {
            Application = f.App, ApplicationId = f.App.Id,
            EnvironmentDefinition = f.Env, EnvironmentDefinitionId = f.Env.Id,
            ApplicationEnvironmentId = Guid.NewGuid(),
            CommitSha = "abc1234", RequestedByUserId = requesterId,
        };

        await f.Sut.NotifyDeploymentStartedAsync(deployment, CancellationToken.None);

        Assert.Single(provider.Sent);
        Assert.Contains("requester@example.local", provider.Sent[0].Recipients);
        Assert.Contains("started", provider.Sent[0].Subject, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task NotifyDeploymentOutcomeAsync_Rollback_UsesRollbackWording()
    {
        var provider = new FakeNotificationProvider(true);
        var f = await CreateFixtureAsync(provider);
        var requesterId = await TestDb.CreateUserWithPermissionsAsync(f.Db, "requester2", PermissionCodes.DeploymentsRollback);
        var deployment = new Deployment
        {
            Application = f.App, ApplicationId = f.App.Id,
            EnvironmentDefinition = f.Env, EnvironmentDefinitionId = f.Env.Id,
            ApplicationEnvironmentId = Guid.NewGuid(),
            CommitSha = "abc1234", RequestedByUserId = requesterId,
            IsRollback = true, Status = DeploymentStatus.Succeeded,
        };

        await f.Sut.NotifyDeploymentOutcomeAsync(deployment, CancellationToken.None);

        Assert.Single(provider.Sent);
        Assert.Contains("Rollback", provider.Sent[0].Subject);
        Assert.Contains("succeeded", provider.Sent[0].Subject);
    }

    [Fact]
    public async Task NotifyDeploymentOutcomeAsync_Failed_IncludesFailureReason_ButNeverASecretValue()
    {
        var provider = new FakeNotificationProvider(true);
        var f = await CreateFixtureAsync(provider);
        var requesterId = await TestDb.CreateUserWithPermissionsAsync(f.Db, "requester3", PermissionCodes.DeploymentsDeployDev);
        var deployment = new Deployment
        {
            Application = f.App, ApplicationId = f.App.Id,
            EnvironmentDefinition = f.Env, EnvironmentDefinitionId = f.Env.Id,
            ApplicationEnvironmentId = Guid.NewGuid(),
            CommitSha = "abc1234", RequestedByUserId = requesterId,
            Status = DeploymentStatus.Failed, FailureReason = "docker compose up failed (exit code 1).",
        };

        await f.Sut.NotifyDeploymentOutcomeAsync(deployment, CancellationToken.None);

        Assert.Single(provider.Sent);
        Assert.Contains("exit code 1", provider.Sent[0].Body);
    }

    [Fact]
    public async Task NotifyDeploymentOutcomeAsync_AuditsOutcomeEvent()
    {
        var provider = new FakeNotificationProvider(true);
        var f = await CreateFixtureAsync(provider);
        var requesterId = await TestDb.CreateUserWithPermissionsAsync(f.Db, "requester4", PermissionCodes.DeploymentsDeployDev);
        var deployment = new Deployment
        {
            Application = f.App, ApplicationId = f.App.Id,
            EnvironmentDefinition = f.Env, EnvironmentDefinitionId = f.Env.Id,
            ApplicationEnvironmentId = Guid.NewGuid(),
            CommitSha = "abc1234", RequestedByUserId = requesterId, Status = DeploymentStatus.Succeeded,
        };

        await f.Sut.NotifyDeploymentOutcomeAsync(deployment, CancellationToken.None);

        var entry = await f.Db.AuditLogs.SingleAsync(a => a.Action == "notification.deployment_outcome");
        Assert.Equal(AuditResult.Success, entry.Result);
    }

    private sealed class FakeNotificationProvider(bool succeeds) : INotificationProvider
    {
        public List<NotificationMessage> Sent { get; } = [];

        public Task<bool> SendAsync(NotificationMessage message, CancellationToken cancellationToken = default)
        {
            Sent.Add(message);
            return Task.FromResult(succeeds);
        }
    }

    private sealed class ThrowingNotificationProvider : INotificationProvider
    {
        public Task<bool> SendAsync(NotificationMessage message, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Simulated provider outage.");
    }

    private sealed class FakeConfiguration : IConfiguration
    {
        public string? this[string key]
        {
            get => null;
            set { }
        }

        public IEnumerable<IConfigurationSection> GetChildren() => [];
        public Microsoft.Extensions.Primitives.IChangeToken GetReloadToken() => throw new NotSupportedException();
        public IConfigurationSection GetSection(string key) => throw new NotSupportedException();
    }
}
