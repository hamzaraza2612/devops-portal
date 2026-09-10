using DevOpsPortal.Application.Abstractions;
using DevOpsPortal.Application.Dtos.Deployments;
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

namespace DevOpsPortal.Tests.Services;

public class DeploymentServiceTests
{
    private sealed record Fixture(
        AppDbContext Db, DeploymentService Sut, FakeCurrentUserService CurrentUser,
        FakeDeploymentJobQueue JobQueue, FakeNotificationProvider NotificationProvider,
        ManagedApplication Application, Dictionary<string, EnvironmentDefinition> Envs,
        Guid DevUserId, Guid QaUserId, Guid UatUserId, Guid DevOpsUserId, Guid CtoUserId, Guid NoPermUserId);

    private static async Task<Fixture> CreateFixtureAsync()
    {
        var db = TestDb.CreateInMemory();
        await TestDb.SeedRolesAndPermissionsAsync(db);
        await TestDb.SeedEnvironmentDefinitionsAsync(db);

        var app = new ManagedApplication { Name = "Sample", Slug = "sample", DeploymentMode = DeploymentMode.LegacyFilesystem };
        db.Applications.Add(app);

        var server = new TargetServer { Name = "server-1", IsActive = true };
        server.AllowedDeploymentRoots.Add(new AllowedDeploymentRoot { RootPath = "/mnt/data/apps", IsActive = true, TargetServerId = server.Id });
        db.TargetServers.Add(server);
        await db.SaveChangesAsync();

        var envs = await db.EnvironmentDefinitions.ToDictionaryAsync(e => e.Name);

        foreach (var envName in new[] { EnvironmentNames.Dev, EnvironmentNames.Qa, EnvironmentNames.Uat, EnvironmentNames.Production })
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
                BranchName = envName.ToLowerInvariant(),
            });
        }
        await db.SaveChangesAsync();

        var devUserId = await TestDb.CreateUserWithPermissionsAsync(db, "dev1",
            PermissionCodes.DeploymentsView, PermissionCodes.DeploymentsDeployDev, PermissionCodes.DeploymentsPromoteQa);
        var qaUserId = await TestDb.CreateUserWithPermissionsAsync(db, "qa1",
            PermissionCodes.DeploymentsView, PermissionCodes.DeploymentsApproveQa, PermissionCodes.DeploymentsDeployQa);
        var uatUserId = await TestDb.CreateUserWithPermissionsAsync(db, "uat1",
            PermissionCodes.DeploymentsView, PermissionCodes.DeploymentsApproveUat, PermissionCodes.DeploymentsDeployUat);
        var devopsUserId = await TestDb.CreateUserWithPermissionsAsync(db, "devops1",
            PermissionCodes.DeploymentsView, PermissionCodes.DeploymentsDeployDev,
            PermissionCodes.DeploymentsPromoteQa, PermissionCodes.DeploymentsApproveQa, PermissionCodes.DeploymentsDeployQa,
            PermissionCodes.DeploymentsPromoteUat, PermissionCodes.DeploymentsApproveUat, PermissionCodes.DeploymentsDeployUat,
            PermissionCodes.DeploymentsPromoteProduction, PermissionCodes.DeploymentsDeployProduction, PermissionCodes.DeploymentsRollback);
        var ctoUserId = await TestDb.CreateUserWithPermissionsAsync(db, "cto1",
            PermissionCodes.DeploymentsView, PermissionCodes.DeploymentsApproveProduction);
        var noPermUserId = await TestDb.CreateUserWithPermissionsAsync(db, "noperm1", PermissionCodes.DeploymentsView);

        var currentUser = new FakeCurrentUserService { UserId = devUserId, Username = "dev1" };
        var jobQueue = new FakeDeploymentJobQueue();
        var notificationProvider = new FakeNotificationProvider();
        var audit = new AuditService(db, currentUser);
        var notificationService = new NotificationService(
            db, [notificationProvider], audit, new FakeConfiguration(), NullLogger<NotificationService>.Instance);

        var sut = new DeploymentService(db, currentUser, audit, jobQueue, notificationService);

        return new Fixture(db, sut, currentUser, jobQueue, notificationProvider, app, envs, devUserId, qaUserId, uatUserId, devopsUserId, ctoUserId, noPermUserId);
    }

    private static async Task<Deployment> InsertSucceededDeploymentAsync(Fixture f, string environmentName, string commitSha)
    {
        var envDef = f.Envs[environmentName];
        var appEnv = await f.Db.ApplicationEnvironments.FirstAsync(ae => ae.ApplicationId == f.Application.Id && ae.EnvironmentDefinitionId == envDef.Id);
        var deployment = new Deployment
        {
            ApplicationId = f.Application.Id,
            EnvironmentDefinitionId = envDef.Id,
            ApplicationEnvironmentId = appEnv.Id,
            CommitSha = commitSha,
            Status = DeploymentStatus.Succeeded,
            RequestedByUserId = f.DevUserId,
            StartedAt = DateTimeOffset.UtcNow,
            CompletedAt = DateTimeOffset.UtcNow,
        };
        f.Db.Deployments.Add(deployment);
        await f.Db.SaveChangesAsync();
        return deployment;
    }

    // ------------------------------------------------------------- DEV deploy

    [Fact]
    public async Task DeployToDevAsync_AsAuthorizedDeveloper_Succeeds()
    {
        var f = await CreateFixtureAsync();

        var dto = await f.Sut.DeployToDevAsync(f.Application.Id, new CreateDevDeploymentRequest("abc1234", "msg", "author", null));

        Assert.Equal(DeploymentStatus.Pending, dto.Status);
        Assert.Equal("abc1234", dto.CommitSha);
        Assert.Contains(dto.Id, f.JobQueue.Enqueued);
    }

    [Fact]
    public async Task DeployToDevAsync_WithoutPermission_ThrowsForbidden()
    {
        var f = await CreateFixtureAsync();
        f.CurrentUser.UserId = f.NoPermUserId;

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            f.Sut.DeployToDevAsync(f.Application.Id, new CreateDevDeploymentRequest("abc1234", null, null, null)));
    }

    [Fact]
    public async Task DeployToDevAsync_WithInvalidCommitSha_ThrowsValidation()
    {
        var f = await CreateFixtureAsync();

        await Assert.ThrowsAsync<ValidationException>(() =>
            f.Sut.DeployToDevAsync(f.Application.Id, new CreateDevDeploymentRequest("not-a-sha!!", null, null, null)));
    }

    [Fact]
    public async Task DeployToDevAsync_SameCommitWhileActive_ThrowsConflict()
    {
        var f = await CreateFixtureAsync();
        await f.Sut.DeployToDevAsync(f.Application.Id, new CreateDevDeploymentRequest("abc1234", null, null, null));

        await Assert.ThrowsAsync<ConflictException>(() =>
            f.Sut.DeployToDevAsync(f.Application.Id, new CreateDevDeploymentRequest("abc1234", null, null, null)));
    }

    [Fact]
    public async Task DeployToDevAsync_DifferentCommitWhileActive_ThrowsConflict()
    {
        var f = await CreateFixtureAsync();
        await f.Sut.DeployToDevAsync(f.Application.Id, new CreateDevDeploymentRequest("abc1234", null, null, null));

        await Assert.ThrowsAsync<ConflictException>(() =>
            f.Sut.DeployToDevAsync(f.Application.Id, new CreateDevDeploymentRequest("def5678", null, null, null)));
    }

    // ------------------------------------------------------------- promotion (QA)

    [Fact]
    public async Task RequestPromotionAsync_Qa_WithSuccessfulDevDeployment_Succeeds()
    {
        var f = await CreateFixtureAsync();
        var devDeployment = await InsertSucceededDeploymentAsync(f, EnvironmentNames.Dev, "abc1234");

        var dto = await f.Sut.RequestPromotionAsync(f.Application.Id, f.Envs[EnvironmentNames.Qa].Id, new CreatePromotionRequest(devDeployment.Id));

        Assert.Equal(ApprovalStatus.PendingApproval, dto.Status);
        Assert.Equal("abc1234", dto.CommitSha);
        Assert.False(dto.RequiresCtoApproval);
    }

    [Fact]
    public async Task RequestPromotionAsync_Qa_WithoutPermission_ThrowsForbidden()
    {
        var f = await CreateFixtureAsync();
        var devDeployment = await InsertSucceededDeploymentAsync(f, EnvironmentNames.Dev, "abc1234");
        f.CurrentUser.UserId = f.NoPermUserId;

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            f.Sut.RequestPromotionAsync(f.Application.Id, f.Envs[EnvironmentNames.Qa].Id, new CreatePromotionRequest(devDeployment.Id)));
    }

    [Fact]
    public async Task RequestPromotionAsync_Qa_WithUnsuccessfulSourceDeployment_ThrowsValidation()
    {
        var f = await CreateFixtureAsync();
        var appEnv = await f.Db.ApplicationEnvironments.FirstAsync(ae => ae.EnvironmentDefinitionId == f.Envs[EnvironmentNames.Dev].Id);
        var failedDeployment = new Deployment
        {
            ApplicationId = f.Application.Id, EnvironmentDefinitionId = f.Envs[EnvironmentNames.Dev].Id, ApplicationEnvironmentId = appEnv.Id,
            CommitSha = "abc1234", Status = DeploymentStatus.Failed, RequestedByUserId = f.DevUserId,
        };
        f.Db.Deployments.Add(failedDeployment);
        await f.Db.SaveChangesAsync();

        await Assert.ThrowsAsync<ValidationException>(() =>
            f.Sut.RequestPromotionAsync(f.Application.Id, f.Envs[EnvironmentNames.Qa].Id, new CreatePromotionRequest(failedDeployment.Id)));
    }

    [Fact]
    public async Task RequestPromotionAsync_Qa_DuplicatePending_ThrowsConflict()
    {
        var f = await CreateFixtureAsync();
        var devDeployment = await InsertSucceededDeploymentAsync(f, EnvironmentNames.Dev, "abc1234");
        await f.Sut.RequestPromotionAsync(f.Application.Id, f.Envs[EnvironmentNames.Qa].Id, new CreatePromotionRequest(devDeployment.Id));

        await Assert.ThrowsAsync<ConflictException>(() =>
            f.Sut.RequestPromotionAsync(f.Application.Id, f.Envs[EnvironmentNames.Qa].Id, new CreatePromotionRequest(devDeployment.Id)));
    }

    [Fact]
    public async Task RequestPromotionAsync_IntoDev_ThrowsValidation()
    {
        var f = await CreateFixtureAsync();
        var devDeployment = await InsertSucceededDeploymentAsync(f, EnvironmentNames.Dev, "abc1234");

        await Assert.ThrowsAsync<ValidationException>(() =>
            f.Sut.RequestPromotionAsync(f.Application.Id, f.Envs[EnvironmentNames.Dev].Id, new CreatePromotionRequest(devDeployment.Id)));
    }

    [Fact]
    public async Task ApprovePromotionAsync_Qa_AsAuthorizedQaUser_Succeeds()
    {
        var f = await CreateFixtureAsync();
        var devDeployment = await InsertSucceededDeploymentAsync(f, EnvironmentNames.Dev, "abc1234");
        var promotion = await f.Sut.RequestPromotionAsync(f.Application.Id, f.Envs[EnvironmentNames.Qa].Id, new CreatePromotionRequest(devDeployment.Id));
        f.CurrentUser.UserId = f.QaUserId;

        var approved = await f.Sut.ApprovePromotionAsync(promotion.Id, new DecidePromotionRequest("looks good"));

        Assert.Equal(ApprovalStatus.Approved, approved.Status);
        Assert.Equal(f.QaUserId, approved.DecidedByUserId);
    }

    [Fact]
    public async Task ApprovePromotionAsync_Qa_WithoutPermission_ThrowsForbidden()
    {
        var f = await CreateFixtureAsync();
        var devDeployment = await InsertSucceededDeploymentAsync(f, EnvironmentNames.Dev, "abc1234");
        var promotion = await f.Sut.RequestPromotionAsync(f.Application.Id, f.Envs[EnvironmentNames.Qa].Id, new CreatePromotionRequest(devDeployment.Id));
        f.CurrentUser.UserId = f.DevUserId; // developer cannot approve QA

        await Assert.ThrowsAsync<ForbiddenException>(() => f.Sut.ApprovePromotionAsync(promotion.Id, new DecidePromotionRequest(null)));
    }

    [Fact]
    public async Task ApprovePromotionAsync_AlreadyDecided_ThrowsConflict()
    {
        var f = await CreateFixtureAsync();
        var devDeployment = await InsertSucceededDeploymentAsync(f, EnvironmentNames.Dev, "abc1234");
        var promotion = await f.Sut.RequestPromotionAsync(f.Application.Id, f.Envs[EnvironmentNames.Qa].Id, new CreatePromotionRequest(devDeployment.Id));
        f.CurrentUser.UserId = f.QaUserId;
        await f.Sut.ApprovePromotionAsync(promotion.Id, new DecidePromotionRequest(null));

        await Assert.ThrowsAsync<ConflictException>(() => f.Sut.ApprovePromotionAsync(promotion.Id, new DecidePromotionRequest(null)));
    }

    [Fact]
    public async Task ApprovePromotionAsync_DoesNotAutoCreateDeployment()
    {
        var f = await CreateFixtureAsync();
        var devDeployment = await InsertSucceededDeploymentAsync(f, EnvironmentNames.Dev, "abc1234");
        var promotion = await f.Sut.RequestPromotionAsync(f.Application.Id, f.Envs[EnvironmentNames.Qa].Id, new CreatePromotionRequest(devDeployment.Id));
        f.CurrentUser.UserId = f.QaUserId;

        await f.Sut.ApprovePromotionAsync(promotion.Id, new DecidePromotionRequest(null));

        var qaDeployments = await f.Db.Deployments.Where(d => d.EnvironmentDefinitionId == f.Envs[EnvironmentNames.Qa].Id).ToListAsync();
        Assert.Empty(qaDeployments);
        Assert.Empty(f.JobQueue.Enqueued);
    }

    [Fact]
    public async Task DeployApprovedPromotionAsync_Qa_WithoutApproval_ThrowsValidation()
    {
        var f = await CreateFixtureAsync();
        var devDeployment = await InsertSucceededDeploymentAsync(f, EnvironmentNames.Dev, "abc1234");
        var promotion = await f.Sut.RequestPromotionAsync(f.Application.Id, f.Envs[EnvironmentNames.Qa].Id, new CreatePromotionRequest(devDeployment.Id));
        f.CurrentUser.UserId = f.QaUserId;

        await Assert.ThrowsAsync<ValidationException>(() => f.Sut.DeployApprovedPromotionAsync(promotion.Id));
    }

    [Fact]
    public async Task DeployApprovedPromotionAsync_Qa_AfterApproval_Succeeds()
    {
        var f = await CreateFixtureAsync();
        var devDeployment = await InsertSucceededDeploymentAsync(f, EnvironmentNames.Dev, "abc1234");
        var promotion = await f.Sut.RequestPromotionAsync(f.Application.Id, f.Envs[EnvironmentNames.Qa].Id, new CreatePromotionRequest(devDeployment.Id));
        f.CurrentUser.UserId = f.QaUserId;
        await f.Sut.ApprovePromotionAsync(promotion.Id, new DecidePromotionRequest(null));

        var deployment = await f.Sut.DeployApprovedPromotionAsync(promotion.Id);

        Assert.Equal("abc1234", deployment.CommitSha);
        Assert.Equal(f.Envs[EnvironmentNames.Qa].Id, deployment.EnvironmentDefinitionId);
        Assert.Contains(deployment.Id, f.JobQueue.Enqueued);
    }

    [Fact]
    public async Task DeployApprovedPromotionAsync_SameApprovedPromotionTwice_ThrowsConflict()
    {
        var f = await CreateFixtureAsync();
        var devDeployment = await InsertSucceededDeploymentAsync(f, EnvironmentNames.Dev, "abc1234");
        var promotion = await f.Sut.RequestPromotionAsync(f.Application.Id, f.Envs[EnvironmentNames.Qa].Id, new CreatePromotionRequest(devDeployment.Id));
        f.CurrentUser.UserId = f.QaUserId;
        await f.Sut.ApprovePromotionAsync(promotion.Id, new DecidePromotionRequest(null));
        await f.Sut.DeployApprovedPromotionAsync(promotion.Id);

        await Assert.ThrowsAsync<ConflictException>(() => f.Sut.DeployApprovedPromotionAsync(promotion.Id));
    }

    // ------------------------------------------------------------- promotion (UAT)

    [Fact]
    public async Task FullUatWorkflow_PromoteApproveDeploy_Succeeds()
    {
        var f = await CreateFixtureAsync();
        await InsertSucceededDeploymentAsync(f, EnvironmentNames.Dev, "abc1234");
        var qaDeployment = await InsertSucceededDeploymentAsync(f, EnvironmentNames.Qa, "abc1234");
        f.CurrentUser.UserId = f.DevOpsUserId;

        var promotion = await f.Sut.RequestPromotionAsync(f.Application.Id, f.Envs[EnvironmentNames.Uat].Id, new CreatePromotionRequest(qaDeployment.Id));
        Assert.Equal(ApprovalStatus.PendingApproval, promotion.Status);

        f.CurrentUser.UserId = f.UatUserId;
        var approved = await f.Sut.ApprovePromotionAsync(promotion.Id, new DecidePromotionRequest(null));
        Assert.Equal(ApprovalStatus.Approved, approved.Status);

        var deployment = await f.Sut.DeployApprovedPromotionAsync(promotion.Id);
        Assert.Equal(f.Envs[EnvironmentNames.Uat].Id, deployment.EnvironmentDefinitionId);
    }

    [Fact]
    public async Task DeployApprovedPromotionAsync_Uat_WithoutApproval_ThrowsValidation()
    {
        var f = await CreateFixtureAsync();
        await InsertSucceededDeploymentAsync(f, EnvironmentNames.Dev, "abc1234");
        var qaDeployment = await InsertSucceededDeploymentAsync(f, EnvironmentNames.Qa, "abc1234");
        f.CurrentUser.UserId = f.DevOpsUserId;
        var promotion = await f.Sut.RequestPromotionAsync(f.Application.Id, f.Envs[EnvironmentNames.Uat].Id, new CreatePromotionRequest(qaDeployment.Id));

        await Assert.ThrowsAsync<ValidationException>(() => f.Sut.DeployApprovedPromotionAsync(promotion.Id));
    }

    [Fact]
    public async Task ApprovePromotionAsync_Uat_WithQaPermissionOnly_ThrowsForbidden()
    {
        var f = await CreateFixtureAsync();
        await InsertSucceededDeploymentAsync(f, EnvironmentNames.Dev, "abc1234");
        var qaDeployment = await InsertSucceededDeploymentAsync(f, EnvironmentNames.Qa, "abc1234");
        f.CurrentUser.UserId = f.DevOpsUserId;
        var promotion = await f.Sut.RequestPromotionAsync(f.Application.Id, f.Envs[EnvironmentNames.Uat].Id, new CreatePromotionRequest(qaDeployment.Id));
        f.CurrentUser.UserId = f.QaUserId; // QA approver cannot approve UAT

        await Assert.ThrowsAsync<ForbiddenException>(() => f.Sut.ApprovePromotionAsync(promotion.Id, new DecidePromotionRequest(null)));
    }

    // ------------------------------------------------------------- promotion (Production / CTO)

    [Fact]
    public async Task RequestPromotionAsync_Production_CreatesCtoApprovalAndSendsEmail()
    {
        var f = await CreateFixtureAsync();
        await InsertSucceededDeploymentAsync(f, EnvironmentNames.Dev, "abc1234");
        await InsertSucceededDeploymentAsync(f, EnvironmentNames.Qa, "abc1234");
        var uatDeployment = await InsertSucceededDeploymentAsync(f, EnvironmentNames.Uat, "abc1234");
        f.CurrentUser.UserId = f.DevOpsUserId;

        var promotion = await f.Sut.RequestPromotionAsync(
            f.Application.Id, f.Envs[EnvironmentNames.Production].Id, new CreatePromotionRequest(uatDeployment.Id));

        Assert.True(promotion.RequiresCtoApproval);
        Assert.Equal(ApprovalStatus.PendingApproval, promotion.CtoApprovalStatus);
        Assert.Single(f.NotificationProvider.Sent);
        Assert.Contains("cto1@example.local", f.NotificationProvider.Sent[0].Recipients);
        Assert.DoesNotContain("password", f.NotificationProvider.Sent[0].Body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DeployApprovedPromotionAsync_Production_WithoutCtoApproval_ThrowsValidation()
    {
        var f = await CreateFixtureAsync();
        await InsertSucceededDeploymentAsync(f, EnvironmentNames.Dev, "abc1234");
        await InsertSucceededDeploymentAsync(f, EnvironmentNames.Qa, "abc1234");
        var uatDeployment = await InsertSucceededDeploymentAsync(f, EnvironmentNames.Uat, "abc1234");
        f.CurrentUser.UserId = f.DevOpsUserId;
        var promotion = await f.Sut.RequestPromotionAsync(
            f.Application.Id, f.Envs[EnvironmentNames.Production].Id, new CreatePromotionRequest(uatDeployment.Id));

        // Not approved by CTO yet — must be rejected regardless of who tries to deploy.
        await Assert.ThrowsAsync<ValidationException>(() => f.Sut.DeployApprovedPromotionAsync(promotion.Id));
    }

    [Fact]
    public async Task ApprovePromotionAsync_Production_DevOpsUserWithoutCtoPermission_ThrowsForbidden()
    {
        var f = await CreateFixtureAsync();
        await InsertSucceededDeploymentAsync(f, EnvironmentNames.Dev, "abc1234");
        await InsertSucceededDeploymentAsync(f, EnvironmentNames.Qa, "abc1234");
        var uatDeployment = await InsertSucceededDeploymentAsync(f, EnvironmentNames.Uat, "abc1234");
        f.CurrentUser.UserId = f.DevOpsUserId;
        var promotion = await f.Sut.RequestPromotionAsync(
            f.Application.Id, f.Envs[EnvironmentNames.Production].Id, new CreatePromotionRequest(uatDeployment.Id));

        // DevOps can deploy production but explicitly does NOT have deployments.approve.production —
        // separation of duties: only the CTO permission can grant production approval.
        await Assert.ThrowsAsync<ForbiddenException>(() => f.Sut.ApprovePromotionAsync(promotion.Id, new DecidePromotionRequest(null)));
    }

    [Fact]
    public async Task ApprovePromotionAsync_Production_AsCto_ApprovesPromotionAndCtoApproval()
    {
        var f = await CreateFixtureAsync();
        await InsertSucceededDeploymentAsync(f, EnvironmentNames.Dev, "abc1234");
        await InsertSucceededDeploymentAsync(f, EnvironmentNames.Qa, "abc1234");
        var uatDeployment = await InsertSucceededDeploymentAsync(f, EnvironmentNames.Uat, "abc1234");
        f.CurrentUser.UserId = f.DevOpsUserId;
        var promotion = await f.Sut.RequestPromotionAsync(
            f.Application.Id, f.Envs[EnvironmentNames.Production].Id, new CreatePromotionRequest(uatDeployment.Id));

        f.CurrentUser.UserId = f.CtoUserId;
        var approved = await f.Sut.ApprovePromotionAsync(promotion.Id, new DecidePromotionRequest("approved for release"));

        Assert.Equal(ApprovalStatus.Approved, approved.Status);
        Assert.Equal(ApprovalStatus.Approved, approved.CtoApprovalStatus);

        // Approval alone must never create a deployment.
        var prodDeployments = await f.Db.Deployments.Where(d => d.EnvironmentDefinitionId == f.Envs[EnvironmentNames.Production].Id).ToListAsync();
        Assert.Empty(prodDeployments);
    }

    [Fact]
    public async Task DeployApprovedPromotionAsync_Production_AfterCtoApproval_AsAuthorizedDevOpsUser_Succeeds()
    {
        var f = await CreateFixtureAsync();
        await InsertSucceededDeploymentAsync(f, EnvironmentNames.Dev, "abc1234");
        await InsertSucceededDeploymentAsync(f, EnvironmentNames.Qa, "abc1234");
        var uatDeployment = await InsertSucceededDeploymentAsync(f, EnvironmentNames.Uat, "abc1234");
        f.CurrentUser.UserId = f.DevOpsUserId;
        var promotion = await f.Sut.RequestPromotionAsync(
            f.Application.Id, f.Envs[EnvironmentNames.Production].Id, new CreatePromotionRequest(uatDeployment.Id));

        f.CurrentUser.UserId = f.CtoUserId;
        await f.Sut.ApprovePromotionAsync(promotion.Id, new DecidePromotionRequest(null));

        f.CurrentUser.UserId = f.DevOpsUserId;
        var deployment = await f.Sut.DeployApprovedPromotionAsync(promotion.Id);

        Assert.Equal(DeploymentStatus.Pending, deployment.Status);
        Assert.Equal(f.Envs[EnvironmentNames.Production].Id, deployment.EnvironmentDefinitionId);
        Assert.Contains(deployment.Id, f.JobQueue.Enqueued);
    }

    [Fact]
    public async Task RejectPromotionAsync_Production_AsCto_RejectsBothRecordsAndBlocksDeploy()
    {
        var f = await CreateFixtureAsync();
        await InsertSucceededDeploymentAsync(f, EnvironmentNames.Dev, "abc1234");
        await InsertSucceededDeploymentAsync(f, EnvironmentNames.Qa, "abc1234");
        var uatDeployment = await InsertSucceededDeploymentAsync(f, EnvironmentNames.Uat, "abc1234");
        f.CurrentUser.UserId = f.DevOpsUserId;
        var promotion = await f.Sut.RequestPromotionAsync(
            f.Application.Id, f.Envs[EnvironmentNames.Production].Id, new CreatePromotionRequest(uatDeployment.Id));

        f.CurrentUser.UserId = f.CtoUserId;
        var rejected = await f.Sut.RejectPromotionAsync(promotion.Id, new DecidePromotionRequest("not ready"));

        Assert.Equal(ApprovalStatus.Rejected, rejected.Status);
        Assert.Equal(ApprovalStatus.Rejected, rejected.CtoApprovalStatus);

        f.CurrentUser.UserId = f.DevOpsUserId;
        await Assert.ThrowsAsync<ValidationException>(() => f.Sut.DeployApprovedPromotionAsync(promotion.Id));
    }

    // ------------------------------------------------------ notifications / approval tokens

    [Fact]
    public async Task RequestPromotionAsync_Qa_NotifiesQaApprovers()
    {
        var f = await CreateFixtureAsync();
        await InsertSucceededDeploymentAsync(f, EnvironmentNames.Dev, "abc1234");
        f.CurrentUser.UserId = f.DevUserId;
        var devDeployment = await f.Db.Deployments.FirstAsync(d => d.CommitSha == "abc1234");

        var promotion = await f.Sut.RequestPromotionAsync(f.Application.Id, f.Envs[EnvironmentNames.Qa].Id, new CreatePromotionRequest(devDeployment.Id));

        Assert.Single(f.NotificationProvider.Sent);
        Assert.Contains("qa1@example.local", f.NotificationProvider.Sent[0].Recipients);
        Assert.NotNull(promotion.NotifiedAt);

        var reloaded = await f.Db.PromotionRequests.SingleAsync(p => p.Id == promotion.Id);
        Assert.Contains("qa1@example.local", reloaded.NotificationRecipients);
    }

    [Fact]
    public async Task RequestPromotionAsync_AuditsNotificationSent()
    {
        var f = await CreateFixtureAsync();
        await InsertSucceededDeploymentAsync(f, EnvironmentNames.Dev, "abc1234");
        f.CurrentUser.UserId = f.DevUserId;
        var devDeployment = await f.Db.Deployments.FirstAsync(d => d.CommitSha == "abc1234");

        await f.Sut.RequestPromotionAsync(f.Application.Id, f.Envs[EnvironmentNames.Qa].Id, new CreatePromotionRequest(devDeployment.Id));

        var entry = await f.Db.AuditLogs.SingleAsync(a => a.Action == "notification.promotion_approval_requested");
        Assert.Equal(AuditResult.Success, entry.Result);
    }

    [Fact]
    public async Task GetPromotionPreviewByTokenAsync_WithValidToken_ReturnsPreview_NeverASecret()
    {
        var f = await CreateFixtureAsync();
        await InsertSucceededDeploymentAsync(f, EnvironmentNames.Dev, "abc1234");
        f.CurrentUser.UserId = f.DevUserId;
        var devDeployment = await f.Db.Deployments.FirstAsync(d => d.CommitSha == "abc1234");
        await f.Sut.RequestPromotionAsync(f.Application.Id, f.Envs[EnvironmentNames.Qa].Id, new CreatePromotionRequest(devDeployment.Id));
        var token = ExtractToken(f.NotificationProvider.Sent[0].Body);

        var preview = await f.Sut.GetPromotionPreviewByTokenAsync(token);

        Assert.Equal("Sample", preview.ApplicationName);
        Assert.Equal(EnvironmentNames.Qa, preview.ToEnvironmentName);
        Assert.Equal("abc1234", preview.CommitSha);
        Assert.Equal(ApprovalStatus.PendingApproval, preview.Status);
        Assert.False(preview.IsExpired);
    }

    [Fact]
    public async Task GetPromotionPreviewByTokenAsync_WithUnknownToken_ThrowsNotFound()
    {
        var f = await CreateFixtureAsync();
        await Assert.ThrowsAsync<NotFoundException>(() => f.Sut.GetPromotionPreviewByTokenAsync("not-a-real-token"));
    }

    [Fact]
    public async Task GetPromotionPreviewByTokenAsync_ReflectsDecisionAfterApproval()
    {
        var f = await CreateFixtureAsync();
        await InsertSucceededDeploymentAsync(f, EnvironmentNames.Dev, "abc1234");
        f.CurrentUser.UserId = f.DevUserId;
        var devDeployment = await f.Db.Deployments.FirstAsync(d => d.CommitSha == "abc1234");
        var promotion = await f.Sut.RequestPromotionAsync(f.Application.Id, f.Envs[EnvironmentNames.Qa].Id, new CreatePromotionRequest(devDeployment.Id));
        var token = ExtractToken(f.NotificationProvider.Sent[0].Body);

        f.CurrentUser.UserId = f.QaUserId;
        await f.Sut.ApprovePromotionAsync(promotion.Id, new DecidePromotionRequest(null));

        var preview = await f.Sut.GetPromotionPreviewByTokenAsync(token);
        Assert.Equal(ApprovalStatus.Approved, preview.Status);
    }

    [Fact]
    public async Task GetPromotionPreviewByTokenAsync_TokenForOneReleaseNeverResolvesAnotherPromotion()
    {
        var f = await CreateFixtureAsync();
        await InsertSucceededDeploymentAsync(f, EnvironmentNames.Dev, "aaa1111");
        f.CurrentUser.UserId = f.DevUserId;
        var devDeploymentA = await f.Db.Deployments.FirstAsync(d => d.CommitSha == "aaa1111");
        var promotionA = await f.Sut.RequestPromotionAsync(f.Application.Id, f.Envs[EnvironmentNames.Qa].Id, new CreatePromotionRequest(devDeploymentA.Id));
        var tokenA = ExtractToken(f.NotificationProvider.Sent[0].Body);

        f.CurrentUser.UserId = f.QaUserId;
        await f.Sut.RejectPromotionAsync(promotionA.Id, new DecidePromotionRequest("superseded"));

        await InsertSucceededDeploymentAsync(f, EnvironmentNames.Dev, "bbb2222");
        f.CurrentUser.UserId = f.DevUserId;
        var devDeploymentB = await f.Db.Deployments.FirstAsync(d => d.CommitSha == "bbb2222");
        var promotionB = await f.Sut.RequestPromotionAsync(f.Application.Id, f.Envs[EnvironmentNames.Qa].Id, new CreatePromotionRequest(devDeploymentB.Id));

        // tokenA must still resolve to promotion A (its own preview), never promotion B —
        // "wrong release" isolation: each token is bound to exactly the request it was minted for.
        var previewA = await f.Sut.GetPromotionPreviewByTokenAsync(tokenA);
        Assert.Equal(promotionA.Id, previewA.Id);
        Assert.NotEqual(promotionB.Id, previewA.Id);
        Assert.Equal("aaa1111", previewA.CommitSha);
    }

    [Fact]
    public async Task GetPromotionPreviewByTokenAsync_ExpiredToken_ReportsExpired_ButStillResolves()
    {
        var f = await CreateFixtureAsync();
        await InsertSucceededDeploymentAsync(f, EnvironmentNames.Dev, "abc1234");
        f.CurrentUser.UserId = f.DevUserId;
        var devDeployment = await f.Db.Deployments.FirstAsync(d => d.CommitSha == "abc1234");
        var promotion = await f.Sut.RequestPromotionAsync(f.Application.Id, f.Envs[EnvironmentNames.Qa].Id, new CreatePromotionRequest(devDeployment.Id));
        var token = ExtractToken(f.NotificationProvider.Sent[0].Body);

        var entity = await f.Db.PromotionRequests.SingleAsync(p => p.Id == promotion.Id);
        entity.ApprovalTokenExpiresAt = DateTimeOffset.UtcNow.AddHours(-1);
        await f.Db.SaveChangesAsync();

        var preview = await f.Sut.GetPromotionPreviewByTokenAsync(token);

        Assert.True(preview.IsExpired);
    }

    [Fact]
    public async Task GetProductionApprovalPreviewByTokenAsync_WithValidToken_ReturnsPreview()
    {
        var f = await CreateFixtureAsync();
        await InsertSucceededDeploymentAsync(f, EnvironmentNames.Dev, "abc1234");
        await InsertSucceededDeploymentAsync(f, EnvironmentNames.Qa, "abc1234");
        var uatDeployment = await InsertSucceededDeploymentAsync(f, EnvironmentNames.Uat, "abc1234");
        f.CurrentUser.UserId = f.DevOpsUserId;
        await f.Sut.RequestPromotionAsync(f.Application.Id, f.Envs[EnvironmentNames.Production].Id, new CreatePromotionRequest(uatDeployment.Id));
        var token = ExtractToken(f.NotificationProvider.Sent[0].Body);

        var preview = await f.Sut.GetProductionApprovalPreviewByTokenAsync(token);

        Assert.Equal("Sample", preview.ApplicationName);
        Assert.Equal(EnvironmentNames.Production, preview.ToEnvironmentName);
        Assert.Equal(ApprovalStatus.PendingApproval, preview.Status);
    }

    [Fact]
    public async Task GetProductionApprovalPreviewByTokenAsync_WithUnknownToken_ThrowsNotFound()
    {
        var f = await CreateFixtureAsync();
        await Assert.ThrowsAsync<NotFoundException>(() => f.Sut.GetProductionApprovalPreviewByTokenAsync("not-a-real-token"));
    }

    [Fact]
    public async Task GetProductionApprovalPreviewByTokenAsync_TokenFromOnePromotionNeverResolvesAnothers()
    {
        var f = await CreateFixtureAsync();
        await InsertSucceededDeploymentAsync(f, EnvironmentNames.Dev, "aaa1111");
        await InsertSucceededDeploymentAsync(f, EnvironmentNames.Qa, "aaa1111");
        var uatDeploymentA = await InsertSucceededDeploymentAsync(f, EnvironmentNames.Uat, "aaa1111");
        f.CurrentUser.UserId = f.DevOpsUserId;
        var promotionA = await f.Sut.RequestPromotionAsync(
            f.Application.Id, f.Envs[EnvironmentNames.Production].Id, new CreatePromotionRequest(uatDeploymentA.Id));
        var tokenA = ExtractToken(f.NotificationProvider.Sent[0].Body);

        f.CurrentUser.UserId = f.CtoUserId;
        await f.Sut.RejectPromotionAsync(promotionA.Id, new DecidePromotionRequest("superseded"));

        await InsertSucceededDeploymentAsync(f, EnvironmentNames.Dev, "bbb2222");
        await InsertSucceededDeploymentAsync(f, EnvironmentNames.Qa, "bbb2222");
        var uatDeploymentB = await InsertSucceededDeploymentAsync(f, EnvironmentNames.Uat, "bbb2222");
        f.CurrentUser.UserId = f.DevOpsUserId;
        var promotionB = await f.Sut.RequestPromotionAsync(
            f.Application.Id, f.Envs[EnvironmentNames.Production].Id, new CreatePromotionRequest(uatDeploymentB.Id));

        // tokenA (for the rejected promotion A / its ProductionApproval) must still
        // resolve only to that same approval — never to promotion B's, even though
        // both are ProductionApproval rows for the same application.
        var previewA = await f.Sut.GetProductionApprovalPreviewByTokenAsync(tokenA);
        Assert.Equal("aaa1111", previewA.CommitSha);
        Assert.NotEqual(promotionB.Id, previewA.Id);
    }

    private static string ExtractToken(string notificationBody)
    {
        var match = System.Text.RegularExpressions.Regex.Match(notificationBody, @"token[=\s]([0-9A-Fa-f]{64})");
        Assert.True(match.Success, $"No approval token found in notification body: {notificationBody}");
        return match.Groups[1].Value;
    }

    // ------------------------------------------------------------- rollback

    [Fact]
    public async Task RollbackAsync_AsAuthorizedUser_Succeeds()
    {
        var f = await CreateFixtureAsync();
        var goodDeployment = await InsertSucceededDeploymentAsync(f, EnvironmentNames.Dev, "goodsha1");
        f.CurrentUser.UserId = f.DevOpsUserId;

        var rollback = await f.Sut.RollbackAsync(
            f.Application.Id, f.Envs[EnvironmentNames.Dev].Id, new RollbackRequest(goodDeployment.Id));

        Assert.True(rollback.IsRollback);
        Assert.Equal(goodDeployment.Id, rollback.RollbackOfDeploymentId);
        Assert.Equal("goodsha1", rollback.CommitSha);
        Assert.Contains(rollback.Id, f.JobQueue.Enqueued);
    }

    [Fact]
    public async Task RollbackAsync_WithoutPermission_ThrowsForbidden()
    {
        var f = await CreateFixtureAsync();
        var goodDeployment = await InsertSucceededDeploymentAsync(f, EnvironmentNames.Dev, "goodsha1");
        f.CurrentUser.UserId = f.DevUserId; // developer has no deployments.rollback

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            f.Sut.RollbackAsync(f.Application.Id, f.Envs[EnvironmentNames.Dev].Id, new RollbackRequest(goodDeployment.Id)));
    }

    [Fact]
    public async Task RollbackAsync_TargetNotSucceeded_ThrowsValidation()
    {
        var f = await CreateFixtureAsync();
        var appEnv = await f.Db.ApplicationEnvironments.FirstAsync(ae => ae.EnvironmentDefinitionId == f.Envs[EnvironmentNames.Dev].Id);
        var failedDeployment = new Deployment
        {
            ApplicationId = f.Application.Id, EnvironmentDefinitionId = f.Envs[EnvironmentNames.Dev].Id, ApplicationEnvironmentId = appEnv.Id,
            CommitSha = "badsha1", Status = DeploymentStatus.Failed, RequestedByUserId = f.DevUserId,
        };
        f.Db.Deployments.Add(failedDeployment);
        await f.Db.SaveChangesAsync();
        f.CurrentUser.UserId = f.DevOpsUserId;

        await Assert.ThrowsAsync<ValidationException>(() =>
            f.Sut.RollbackAsync(f.Application.Id, f.Envs[EnvironmentNames.Dev].Id, new RollbackRequest(failedDeployment.Id)));
    }

    [Fact]
    public async Task RollbackAsync_WhileActiveDeploymentExists_ThrowsConflict()
    {
        var f = await CreateFixtureAsync();
        var goodDeployment = await InsertSucceededDeploymentAsync(f, EnvironmentNames.Dev, "goodsha1");
        await f.Sut.DeployToDevAsync(f.Application.Id, new CreateDevDeploymentRequest("cafe1234", null, null, null));
        f.CurrentUser.UserId = f.DevOpsUserId;

        await Assert.ThrowsAsync<ConflictException>(() =>
            f.Sut.RollbackAsync(f.Application.Id, f.Envs[EnvironmentNames.Dev].Id, new RollbackRequest(goodDeployment.Id)));
    }

    // ------------------------------------------------------------- status / audit

    [Fact]
    public async Task GetStatusAsync_ReturnsCurrentAndLatestAndPendingPromotion()
    {
        var f = await CreateFixtureAsync();
        await InsertSucceededDeploymentAsync(f, EnvironmentNames.Dev, "abc1234");

        var status = await f.Sut.GetStatusAsync(f.Application.Id, f.Envs[EnvironmentNames.Dev].Id);

        Assert.NotNull(status.CurrentDeployment);
        Assert.Equal("abc1234", status.CurrentDeployment!.CommitSha);
        Assert.NotNull(status.LatestAttempt);
        Assert.Null(status.PendingPromotionRequestId);
    }

    [Fact]
    public async Task AuditLog_RecordsKeyLifecycleEvents()
    {
        var f = await CreateFixtureAsync();
        var devDeployment = await InsertSucceededDeploymentAsync(f, EnvironmentNames.Dev, "abc1234");

        var promotion = await f.Sut.RequestPromotionAsync(f.Application.Id, f.Envs[EnvironmentNames.Qa].Id, new CreatePromotionRequest(devDeployment.Id));
        f.CurrentUser.UserId = f.QaUserId;
        await f.Sut.ApprovePromotionAsync(promotion.Id, new DecidePromotionRequest(null));
        await f.Sut.DeployApprovedPromotionAsync(promotion.Id);

        var actions = await f.Db.AuditLogs.Select(a => a.Action).ToListAsync();
        Assert.Contains("promotion.requested", actions);
        Assert.Contains("promotion.approved", actions);
        Assert.Contains("deployment.requested", actions);
    }

    private sealed class FakeDeploymentJobQueue : IDeploymentJobQueue
    {
        public List<Guid> Enqueued { get; } = [];
        public void Enqueue(Guid deploymentId) => Enqueued.Add(deploymentId);
        public Task<Guid> DequeueAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class FakeNotificationProvider : INotificationProvider
    {
        public List<NotificationMessage> Sent { get; } = [];

        public Task<bool> SendAsync(NotificationMessage message, CancellationToken cancellationToken = default)
        {
            Sent.Add(message);
            return Task.FromResult(true);
        }
    }

    private sealed class FakeConfiguration : IConfiguration
    {
        public string? this[string key]
        {
            get => null;
            set { }
        }

        public IEnumerable<IConfigurationSection> GetChildren() => [];
        public IChangeToken GetReloadToken() => throw new NotSupportedException();
        public IConfigurationSection GetSection(string key) => throw new NotSupportedException();
    }
}
