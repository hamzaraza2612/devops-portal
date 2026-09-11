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

namespace DevOpsPortal.Tests.Services;

/// <summary>Covers PromotionRequest.FromBranch/ToBranch/BranchPromotionSucceeded —
/// the git-level half of RequestPromotionAsync (see DeploymentService.ResolveBranchesAsync
/// and its call site). The DB-level promotion workflow (approve/deploy) is already covered
/// by DeploymentServiceTests; these tests focus specifically on branch resolution and the
/// "never blocks the workflow" contract.</summary>
public class BranchPromotionTests
{
    private sealed record Fixture(
        AppDbContext Db, DeploymentService Sut, ManagedApplication Application, Repository Repository,
        Dictionary<string, EnvironmentDefinition> Envs, Guid DevUserId, Deployment SucceededDevDeployment);

    private static async Task<Fixture> CreateFixtureAsync(IGitProviderClient? gitProviderClient = null, bool withRepository = true, bool withBranches = true)
    {
        var db = TestDb.CreateInMemory();
        await TestDb.SeedRolesAndPermissionsAsync(db);
        await TestDb.SeedEnvironmentDefinitionsAsync(db);

        var repository = new Repository { Name = "sample-repo", Url = "https://gitlab.example.com/group/sample.git", Provider = RepositoryProvider.GitLab, AccessTokenEnvVarName = "GITLAB_TOKEN_SAMPLE" };
        db.Repositories.Add(repository);

        var app = new ManagedApplication
        {
            Name = "Sample", Slug = "sample", DeploymentMode = DeploymentMode.LegacyFilesystem,
            RepositoryId = withRepository ? repository.Id : null,
        };
        db.Applications.Add(app);

        var server = new TargetServer { Name = "server-1", IsActive = true };
        server.AllowedDeploymentRoots.Add(new AllowedDeploymentRoot { RootPath = "/mnt/data/apps", IsActive = true, TargetServerId = server.Id });
        db.TargetServers.Add(server);
        await db.SaveChangesAsync();

        var envs = await db.EnvironmentDefinitions.ToDictionaryAsync(e => e.Name);
        foreach (var envName in new[] { EnvironmentNames.Dev, EnvironmentNames.Qa })
        {
            db.ApplicationEnvironments.Add(new ApplicationEnvironment
            {
                ApplicationId = app.Id,
                EnvironmentDefinitionId = envs[envName].Id,
                TargetServerId = server.Id,
                DeploymentRootPath = "/mnt/data/apps/sample",
                PublishSubPath = "publish",
                BackupSubPath = "Backups",
                ComposeFilePath = "docker-compose.yml",
                IsActive = true,
                HealthCheckType = HealthCheckType.None,
                BranchName = withBranches ? envName.ToLowerInvariant() : null,
            });
        }
        await db.SaveChangesAsync();

        var devUserId = await TestDb.CreateUserWithPermissionsAsync(db, "dev1",
            PermissionCodes.DeploymentsView, PermissionCodes.DeploymentsDeployDev, PermissionCodes.DeploymentsPromoteQa);

        var currentUser = new FakeCurrentUserService { UserId = devUserId, Username = "dev1" };
        var currentTenant = new FakeCurrentTenantService();
        var jobQueue = new NoopJobQueue();
        var audit = new AuditService(db, currentUser, currentTenant);
        var notificationService = new NotificationService(db, [], audit, new TestConfiguration(), NullLogger<NotificationService>.Instance);
        var sut = new DeploymentService(db, currentUser, currentTenant, audit, jobQueue, notificationService, gitProviderClient ?? new FakeGitProviderClient());

        var deployment = new Deployment
        {
            ApplicationId = app.Id, EnvironmentDefinitionId = envs[EnvironmentNames.Dev].Id,
            ApplicationEnvironmentId = Guid.NewGuid(), CommitSha = "abc1234", Status = DeploymentStatus.Succeeded,
            RequestedByUserId = devUserId,
        };
        db.Deployments.Add(deployment);
        await db.SaveChangesAsync();

        return new Fixture(db, sut, app, repository, envs, devUserId, deployment);
    }

    [Fact]
    public async Task RequestPromotionAsync_WithConfiguredBranchesAndRepository_AttemptsBranchPromotion_AndRecordsSuccess()
    {
        var f = await CreateFixtureAsync(new FakeGitProviderClient(GitProviderResult<string>.Ok("mergecommit123")));

        var promotion = await f.Sut.RequestPromotionAsync(f.Application.Id, f.Envs[EnvironmentNames.Qa].Id, new(f.SucceededDevDeployment.Id));

        Assert.Equal("dev", promotion.FromBranch);
        Assert.Equal("qa", promotion.ToBranch);
        Assert.True(promotion.BranchPromotionSucceeded);
        Assert.Equal("mergecommit123", promotion.BranchPromotionDetail);
    }

    [Fact]
    public async Task RequestPromotionAsync_WhenBranchPromotionFails_StillSucceeds_AndRecordsFailureDetail()
    {
        var f = await CreateFixtureAsync(new FakeGitProviderClient(GitProviderResult<string>.Fail("merge conflict")));

        var promotion = await f.Sut.RequestPromotionAsync(f.Application.Id, f.Envs[EnvironmentNames.Qa].Id, new(f.SucceededDevDeployment.Id));

        Assert.False(promotion.BranchPromotionSucceeded);
        Assert.Contains("merge conflict", promotion.BranchPromotionDetail);
        // The DB-level promotion itself is never blocked by a git-level failure.
        Assert.Equal(ApprovalStatus.PendingApproval, promotion.Status);
    }

    [Fact]
    public async Task RequestPromotionAsync_WithNoRepositoryConfigured_SkipsBranchPromotion_LeavesSucceededNull()
    {
        var f = await CreateFixtureAsync(withRepository: false);

        var promotion = await f.Sut.RequestPromotionAsync(f.Application.Id, f.Envs[EnvironmentNames.Qa].Id, new(f.SucceededDevDeployment.Id));

        Assert.Equal("dev", promotion.FromBranch);
        Assert.Equal("qa", promotion.ToBranch);
        Assert.Null(promotion.BranchPromotionSucceeded);
    }

    [Fact]
    public async Task RequestPromotionAsync_WithNoBranchesConfigured_SkipsBranchPromotion_LeavesBranchFieldsNull()
    {
        var f = await CreateFixtureAsync(withBranches: false);

        var promotion = await f.Sut.RequestPromotionAsync(f.Application.Id, f.Envs[EnvironmentNames.Qa].Id, new(f.SucceededDevDeployment.Id));

        Assert.Null(promotion.FromBranch);
        Assert.Null(promotion.ToBranch);
        Assert.Null(promotion.BranchPromotionSucceeded);
    }

    private sealed class NoopJobQueue : IDeploymentJobQueue
    {
        public void Enqueue(DeploymentJob job) { }
        public Task<DeploymentJob> DequeueAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class TestConfiguration : Microsoft.Extensions.Configuration.IConfiguration
    {
        public string? this[string key] { get => null; set { } }
        public IEnumerable<Microsoft.Extensions.Configuration.IConfigurationSection> GetChildren() => [];
        public Microsoft.Extensions.Primitives.IChangeToken GetReloadToken() => throw new NotSupportedException();
        public Microsoft.Extensions.Configuration.IConfigurationSection GetSection(string key) => throw new NotSupportedException();
    }
}
