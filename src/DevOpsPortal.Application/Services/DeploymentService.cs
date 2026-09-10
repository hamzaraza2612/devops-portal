using System.Linq.Expressions;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using DevOpsPortal.Application.Abstractions;
using DevOpsPortal.Application.Common;
using DevOpsPortal.Application.Dtos.Deployments;
using DevOpsPortal.Application.Exceptions;
using DevOpsPortal.Domain.Constants;
using DevOpsPortal.Domain.Entities;
using DevOpsPortal.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace DevOpsPortal.Application.Services;

public partial class DeploymentService(
    IAppDbContext db,
    ICurrentUserService currentUser,
    IAuditService auditService,
    IDeploymentJobQueue jobQueue,
    IEmailSender emailSender,
    IConfiguration configuration,
    ILogger<DeploymentService> logger) : IDeploymentService
{
    private static readonly Dictionary<string, string> PromotePermissionByEnvironment = new()
    {
        [EnvironmentNames.Qa] = PermissionCodes.DeploymentsPromoteQa,
        [EnvironmentNames.Uat] = PermissionCodes.DeploymentsPromoteUat,
        [EnvironmentNames.Production] = PermissionCodes.DeploymentsPromoteProduction,
    };

    private static readonly Dictionary<string, string> ApprovePermissionByEnvironment = new()
    {
        [EnvironmentNames.Qa] = PermissionCodes.DeploymentsApproveQa,
        [EnvironmentNames.Uat] = PermissionCodes.DeploymentsApproveUat,
        [EnvironmentNames.Production] = PermissionCodes.DeploymentsApproveProduction,
    };

    private static readonly Dictionary<string, string> DeployPermissionByEnvironment = new()
    {
        [EnvironmentNames.Qa] = PermissionCodes.DeploymentsDeployQa,
        [EnvironmentNames.Uat] = PermissionCodes.DeploymentsDeployUat,
        [EnvironmentNames.Production] = PermissionCodes.DeploymentsDeployProduction,
    };

    // ----------------------------------------------------------------- reads

    public async Task<IReadOnlyList<DeploymentDto>> ListAsync(
        Guid? applicationId, Guid? environmentDefinitionId, DeploymentStatus? status, CancellationToken cancellationToken = default)
    {
        var query = db.Deployments.AsQueryable();
        if (applicationId is not null) query = query.Where(d => d.ApplicationId == applicationId);
        if (environmentDefinitionId is not null) query = query.Where(d => d.EnvironmentDefinitionId == environmentDefinitionId);
        if (status is not null) query = query.Where(d => d.Status == status);

        return await query.OrderByDescending(d => d.RequestedAt).Select(DeploymentProjection()).ToListAsync(cancellationToken);
    }

    public async Task<DeploymentDto> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        await db.Deployments.Where(d => d.Id == id).Select(DeploymentProjection()).FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("Deployment", id);

    public async Task<IReadOnlyList<DeploymentLogEntryDto>> GetLogsAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (!await db.Deployments.AnyAsync(d => d.Id == id, cancellationToken))
            throw new NotFoundException("Deployment", id);

        return await db.DeploymentLogEntries
            .Where(l => l.DeploymentId == id)
            .OrderBy(l => l.Sequence)
            .Select(l => new DeploymentLogEntryDto(l.Sequence, l.Timestamp, l.Level, l.Message))
            .ToListAsync(cancellationToken);
    }

    public async Task<DeploymentStatusSummaryDto> GetStatusAsync(
        Guid applicationId, Guid environmentDefinitionId, CancellationToken cancellationToken = default)
    {
        var envDef = await db.EnvironmentDefinitions.FirstOrDefaultAsync(e => e.Id == environmentDefinitionId, cancellationToken)
            ?? throw new NotFoundException("EnvironmentDefinition", environmentDefinitionId);
        if (!await db.Applications.AnyAsync(a => a.Id == applicationId, cancellationToken))
            throw new NotFoundException("Application", applicationId);

        var current = await db.Deployments
            .Where(d => d.ApplicationId == applicationId && d.EnvironmentDefinitionId == environmentDefinitionId && d.Status == DeploymentStatus.Succeeded)
            .OrderByDescending(d => d.CompletedAt)
            .Select(DeploymentProjection())
            .FirstOrDefaultAsync(cancellationToken);

        var latest = await db.Deployments
            .Where(d => d.ApplicationId == applicationId && d.EnvironmentDefinitionId == environmentDefinitionId)
            .OrderByDescending(d => d.RequestedAt)
            .Select(DeploymentProjection())
            .FirstOrDefaultAsync(cancellationToken);

        var pendingPromotionId = await db.PromotionRequests
            .Where(p => p.ApplicationId == applicationId && p.ToEnvironmentDefinitionId == environmentDefinitionId && p.Status == ApprovalStatus.PendingApproval)
            .OrderByDescending(p => p.RequestedAt)
            .Select(p => (Guid?)p.Id)
            .FirstOrDefaultAsync(cancellationToken);

        return new DeploymentStatusSummaryDto(applicationId, environmentDefinitionId, envDef.Name, current, latest, pendingPromotionId);
    }

    public async Task<IReadOnlyList<PromotionRequestDto>> ListPendingPromotionsAsync(
        Guid? applicationId, Guid? toEnvironmentDefinitionId, bool includeApprovedAwaitingDeploy = false, CancellationToken cancellationToken = default)
    {
        var query = includeApprovedAwaitingDeploy
            ? db.PromotionRequests.Where(p =>
                p.Status == ApprovalStatus.PendingApproval ||
                (p.Status == ApprovalStatus.Approved && !db.Deployments.Any(d => d.PromotionRequestId == p.Id)))
            : db.PromotionRequests.Where(p => p.Status == ApprovalStatus.PendingApproval);

        if (applicationId is not null) query = query.Where(p => p.ApplicationId == applicationId);
        if (toEnvironmentDefinitionId is not null) query = query.Where(p => p.ToEnvironmentDefinitionId == toEnvironmentDefinitionId);

        return await query.OrderBy(p => p.RequestedAt).Select(PromotionProjection()).ToListAsync(cancellationToken);
    }

    public async Task<PromotionRequestDto> GetPromotionAsync(Guid promotionRequestId, CancellationToken cancellationToken = default) =>
        await db.PromotionRequests.Where(p => p.Id == promotionRequestId).Select(PromotionProjection()).FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("PromotionRequest", promotionRequestId);

    // ------------------------------------------------------------ DEV deploy

    public async Task<DeploymentDto> DeployToDevAsync(
        Guid applicationId, CreateDevDeploymentRequest request, CancellationToken cancellationToken = default)
    {
        var userId = RequireUserId();
        await EnsurePermissionAsync(userId, PermissionCodes.DeploymentsDeployDev, cancellationToken);

        var application = await db.Applications.FirstOrDefaultAsync(a => a.Id == applicationId, cancellationToken)
            ?? throw new NotFoundException("Application", applicationId);
        if (!application.IsActive)
            throw new ValidationException("Application is not active.");

        var devEnvDef = await db.EnvironmentDefinitions.FirstOrDefaultAsync(e => e.Name == EnvironmentNames.Dev, cancellationToken)
            ?? throw new ValidationException("DEV environment reference data is not configured.");

        var appEnv = await LoadActiveEnvironmentAsync(applicationId, devEnvDef.Id, cancellationToken)
            ?? throw new ValidationException("DEV is not configured for this application.");

        var commitSha = ValidateCommitSha(request.CommitSha);
        await EnsureNoActiveDeploymentAsync(applicationId, devEnvDef.Id, commitSha, cancellationToken);

        var deployment = new Deployment
        {
            ApplicationId = applicationId,
            EnvironmentDefinitionId = devEnvDef.Id,
            ApplicationEnvironmentId = appEnv.Id,
            CommitSha = commitSha,
            CommitMessage = string.IsNullOrWhiteSpace(request.CommitMessage) ? null : request.CommitMessage.Trim(),
            CommitAuthor = string.IsNullOrWhiteSpace(request.CommitAuthor) ? null : request.CommitAuthor.Trim(),
            Branch = string.IsNullOrWhiteSpace(request.Branch) ? appEnv.BranchName : request.Branch.Trim(),
            RequestedByUserId = userId,
        };

        await SaveAndEnqueueAsync(deployment, cancellationToken);

        await auditService.LogAsync("deployment.requested", AuditResult.Success, "Deployment", deployment.Id.ToString(),
            details: $"{application.Name}/DEV commit {commitSha}", cancellationToken: cancellationToken);

        return await GetAsync(deployment.Id, cancellationToken);
    }

    // ------------------------------------------------------------ promotion

    public async Task<PromotionRequestDto> RequestPromotionAsync(
        Guid applicationId, Guid toEnvironmentDefinitionId, CreatePromotionRequest request, CancellationToken cancellationToken = default)
    {
        var userId = RequireUserId();

        var application = await db.Applications.FirstOrDefaultAsync(a => a.Id == applicationId, cancellationToken)
            ?? throw new NotFoundException("Application", applicationId);

        var toEnv = await db.EnvironmentDefinitions.FirstOrDefaultAsync(e => e.Id == toEnvironmentDefinitionId, cancellationToken)
            ?? throw new NotFoundException("EnvironmentDefinition", toEnvironmentDefinitionId);

        if (!PromotePermissionByEnvironment.TryGetValue(toEnv.Name, out var permissionCode))
            throw new ValidationException($"Cannot request a promotion into '{toEnv.Name}'.");
        await EnsurePermissionAsync(userId, permissionCode, cancellationToken);

        var fromEnv = await db.EnvironmentDefinitions.FirstOrDefaultAsync(e => e.SortOrder == toEnv.SortOrder - 1, cancellationToken)
            ?? throw new ValidationException($"No environment precedes '{toEnv.Name}' in the pipeline.");

        var sourceDeployment = await db.Deployments.FirstOrDefaultAsync(d =>
                d.Id == request.SourceDeploymentId && d.ApplicationId == applicationId &&
                d.EnvironmentDefinitionId == fromEnv.Id && d.Status == DeploymentStatus.Succeeded, cancellationToken)
            ?? throw new ValidationException($"SourceDeploymentId must reference a successful deployment of this application in {fromEnv.Name}.");

        var alreadyPending = await db.PromotionRequests.AnyAsync(p =>
            p.ApplicationId == applicationId && p.ToEnvironmentDefinitionId == toEnvironmentDefinitionId &&
            p.Status == ApprovalStatus.PendingApproval, cancellationToken);
        if (alreadyPending)
            throw new ConflictException($"A promotion request into '{toEnv.Name}' is already pending approval.");

        var promotion = new PromotionRequest
        {
            ApplicationId = applicationId,
            FromEnvironmentDefinitionId = fromEnv.Id,
            ToEnvironmentDefinitionId = toEnvironmentDefinitionId,
            SourceDeploymentId = sourceDeployment.Id,
            CommitSha = sourceDeployment.CommitSha,
            RequestedByUserId = userId,
        };
        db.PromotionRequests.Add(promotion);
        await db.SaveChangesAsync(cancellationToken);

        await auditService.LogAsync("promotion.requested", AuditResult.Success, "PromotionRequest", promotion.Id.ToString(),
            details: $"{application.Name}: {fromEnv.Name} -> {toEnv.Name}, commit {promotion.CommitSha}", cancellationToken: cancellationToken);

        if (toEnv.IsProductionLike)
            await CreateProductionApprovalAsync(promotion, application, cancellationToken);

        return await GetPromotionAsync(promotion.Id, cancellationToken);
    }

    public async Task<PromotionRequestDto> ApprovePromotionAsync(
        Guid promotionRequestId, DecidePromotionRequest request, CancellationToken cancellationToken = default)
    {
        var userId = RequireUserId();

        var promotion = await db.PromotionRequests
            .Include(p => p.ToEnvironmentDefinition)
            .Include(p => p.ProductionApproval)
            .FirstOrDefaultAsync(p => p.Id == promotionRequestId, cancellationToken)
            ?? throw new NotFoundException("PromotionRequest", promotionRequestId);

        if (!ApprovePermissionByEnvironment.TryGetValue(promotion.ToEnvironmentDefinition.Name, out var permissionCode))
            throw new ValidationException($"Cannot approve a promotion into '{promotion.ToEnvironmentDefinition.Name}'.");
        await EnsurePermissionAsync(userId, permissionCode, cancellationToken);

        if (promotion.ToEnvironmentDefinition.IsProductionLike)
        {
            if (promotion.ProductionApproval is null)
                throw new ValidationException("This production promotion has no linked approval record.");

            if (!await TryTransitionProductionApprovalAsync(promotion.ProductionApproval.Id, ApprovalStatus.Approved, userId, request.Notes, cancellationToken))
                throw new ConflictException("This production approval has already been decided.");
            if (!await TryTransitionPromotionAsync(promotionRequestId, ApprovalStatus.Approved, userId, request.Notes, cancellationToken))
                throw new ConflictException("This promotion request has already been decided.");

            await auditService.LogAsync("production_approval.granted", AuditResult.Success, "ProductionApproval",
                promotion.ProductionApproval.Id.ToString(), details: $"Promotion {promotionRequestId} commit {promotion.CommitSha}",
                cancellationToken: cancellationToken);
        }
        else
        {
            if (!await TryTransitionPromotionAsync(promotionRequestId, ApprovalStatus.Approved, userId, request.Notes, cancellationToken))
                throw new ConflictException("This promotion request has already been decided.");

            await auditService.LogAsync("promotion.approved", AuditResult.Success, "PromotionRequest", promotionRequestId.ToString(),
                details: $"commit {promotion.CommitSha}", cancellationToken: cancellationToken);
        }

        return await GetPromotionAsync(promotionRequestId, cancellationToken);
    }

    public async Task<PromotionRequestDto> RejectPromotionAsync(
        Guid promotionRequestId, DecidePromotionRequest request, CancellationToken cancellationToken = default)
    {
        var userId = RequireUserId();

        var promotion = await db.PromotionRequests
            .Include(p => p.ToEnvironmentDefinition)
            .Include(p => p.ProductionApproval)
            .FirstOrDefaultAsync(p => p.Id == promotionRequestId, cancellationToken)
            ?? throw new NotFoundException("PromotionRequest", promotionRequestId);

        if (!ApprovePermissionByEnvironment.TryGetValue(promotion.ToEnvironmentDefinition.Name, out var permissionCode))
            throw new ValidationException($"Cannot reject a promotion into '{promotion.ToEnvironmentDefinition.Name}'.");
        await EnsurePermissionAsync(userId, permissionCode, cancellationToken);

        if (promotion.ToEnvironmentDefinition.IsProductionLike && promotion.ProductionApproval is not null)
        {
            if (!await TryTransitionProductionApprovalAsync(promotion.ProductionApproval.Id, ApprovalStatus.Rejected, userId, request.Notes, cancellationToken))
                throw new ConflictException("This production approval has already been decided.");
            if (!await TryTransitionPromotionAsync(promotionRequestId, ApprovalStatus.Rejected, userId, request.Notes, cancellationToken))
                throw new ConflictException("This promotion request has already been decided.");

            await auditService.LogAsync("production_approval.rejected", AuditResult.Success, "ProductionApproval",
                promotion.ProductionApproval.Id.ToString(), details: $"Promotion {promotionRequestId} commit {promotion.CommitSha}",
                cancellationToken: cancellationToken);
        }
        else
        {
            if (!await TryTransitionPromotionAsync(promotionRequestId, ApprovalStatus.Rejected, userId, request.Notes, cancellationToken))
                throw new ConflictException("This promotion request has already been decided.");

            await auditService.LogAsync("promotion.rejected", AuditResult.Success, "PromotionRequest", promotionRequestId.ToString(),
                details: $"commit {promotion.CommitSha}", cancellationToken: cancellationToken);
        }

        return await GetPromotionAsync(promotionRequestId, cancellationToken);
    }

    public async Task<DeploymentDto> DeployApprovedPromotionAsync(Guid promotionRequestId, CancellationToken cancellationToken = default)
    {
        var userId = RequireUserId();

        var promotion = await db.PromotionRequests
            .Include(p => p.ToEnvironmentDefinition)
            .Include(p => p.ProductionApproval)
            .Include(p => p.SourceDeployment)
            .Include(p => p.Application)
            .FirstOrDefaultAsync(p => p.Id == promotionRequestId, cancellationToken)
            ?? throw new NotFoundException("PromotionRequest", promotionRequestId);

        if (!DeployPermissionByEnvironment.TryGetValue(promotion.ToEnvironmentDefinition.Name, out var permissionCode))
            throw new ValidationException($"Cannot deploy to '{promotion.ToEnvironmentDefinition.Name}' via promotion.");
        await EnsurePermissionAsync(userId, permissionCode, cancellationToken);

        if (promotion.Status != ApprovalStatus.Approved)
            throw new ValidationException("This promotion request has not been approved yet.");

        if (promotion.ToEnvironmentDefinition.IsProductionLike && promotion.ProductionApproval?.Status != ApprovalStatus.Approved)
            throw new ValidationException("CTO approval has not been granted for this production promotion yet.");

        var alreadyDeployed = await db.Deployments.AnyAsync(d =>
            d.PromotionRequestId == promotionRequestId &&
            (d.Status == DeploymentStatus.Pending || d.Status == DeploymentStatus.Queued ||
             d.Status == DeploymentStatus.Running || d.Status == DeploymentStatus.Succeeded), cancellationToken);
        if (alreadyDeployed)
            throw new ConflictException("This approved promotion has already been deployed, or is currently deploying.");

        var appEnv = await LoadActiveEnvironmentAsync(promotion.ApplicationId, promotion.ToEnvironmentDefinitionId, cancellationToken)
            ?? throw new ValidationException($"{promotion.ToEnvironmentDefinition.Name} is not configured for this application.");

        await EnsureNoActiveDeploymentAsync(promotion.ApplicationId, promotion.ToEnvironmentDefinitionId, promotion.CommitSha, cancellationToken);

        var deployment = new Deployment
        {
            ApplicationId = promotion.ApplicationId,
            EnvironmentDefinitionId = promotion.ToEnvironmentDefinitionId,
            ApplicationEnvironmentId = appEnv.Id,
            CommitSha = promotion.CommitSha,
            CommitMessage = promotion.SourceDeployment.CommitMessage,
            CommitAuthor = promotion.SourceDeployment.CommitAuthor,
            Branch = appEnv.BranchName ?? promotion.SourceDeployment.Branch,
            PromotionRequestId = promotion.Id,
            RequestedByUserId = userId,
        };

        await SaveAndEnqueueAsync(deployment, cancellationToken);

        await auditService.LogAsync("deployment.requested", AuditResult.Success, "Deployment", deployment.Id.ToString(),
            details: $"{promotion.Application.Name}/{promotion.ToEnvironmentDefinition.Name} commit {promotion.CommitSha} (promotion {promotion.Id})",
            cancellationToken: cancellationToken);

        return await GetAsync(deployment.Id, cancellationToken);
    }

    // ------------------------------------------------------------- rollback

    public async Task<DeploymentDto> RollbackAsync(
        Guid applicationId, Guid environmentDefinitionId, RollbackRequest request, CancellationToken cancellationToken = default)
    {
        var userId = RequireUserId();
        await EnsurePermissionAsync(userId, PermissionCodes.DeploymentsRollback, cancellationToken);

        var application = await db.Applications.FirstOrDefaultAsync(a => a.Id == applicationId, cancellationToken)
            ?? throw new NotFoundException("Application", applicationId);

        var envDef = await db.EnvironmentDefinitions.FirstOrDefaultAsync(e => e.Id == environmentDefinitionId, cancellationToken)
            ?? throw new NotFoundException("EnvironmentDefinition", environmentDefinitionId);

        var target = await db.Deployments.FirstOrDefaultAsync(d =>
                d.Id == request.TargetDeploymentId && d.ApplicationId == applicationId &&
                d.EnvironmentDefinitionId == environmentDefinitionId && d.Status == DeploymentStatus.Succeeded, cancellationToken)
            ?? throw new ValidationException(
                "TargetDeploymentId must reference a previously successful deployment of this application in this environment.");

        var appEnv = await LoadActiveEnvironmentAsync(applicationId, environmentDefinitionId, cancellationToken)
            ?? throw new ValidationException($"{envDef.Name} is not configured for this application.");

        await EnsureNoActiveDeploymentAsync(applicationId, environmentDefinitionId, target.CommitSha, cancellationToken);

        var deployment = new Deployment
        {
            ApplicationId = applicationId,
            EnvironmentDefinitionId = environmentDefinitionId,
            ApplicationEnvironmentId = appEnv.Id,
            CommitSha = target.CommitSha,
            CommitMessage = target.CommitMessage,
            CommitAuthor = target.CommitAuthor,
            Branch = target.Branch,
            IsRollback = true,
            RollbackOfDeploymentId = target.Id,
            RequestedByUserId = userId,
        };

        await SaveAndEnqueueAsync(deployment, cancellationToken);

        await auditService.LogAsync("rollback.requested", AuditResult.Success, "Deployment", deployment.Id.ToString(),
            details: $"{application.Name}/{envDef.Name} rollback to commit {target.CommitSha} (from deployment {target.Id})",
            cancellationToken: cancellationToken);

        return await GetAsync(deployment.Id, cancellationToken);
    }

    // -------------------------------------------------------------- helpers

    private async Task CreateProductionApprovalAsync(PromotionRequest promotion, ManagedApplication application, CancellationToken cancellationToken)
    {
        var approval = new ProductionApproval
        {
            PromotionRequestId = promotion.Id,
            ApprovalToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)),
        };
        db.ProductionApprovals.Add(approval);
        await db.SaveChangesAsync(cancellationToken);

        await auditService.LogAsync("production_approval.requested", AuditResult.Success, "ProductionApproval", approval.Id.ToString(),
            details: $"{application.Name} commit {promotion.CommitSha}", cancellationToken: cancellationToken);

        // Permission-based, not role-name-based: whoever currently holds
        // deployments.approve.production gets notified, consistent with how every
        // other authorization check in this service works (not a hardcoded "CTO"
        // role — a tenant could grant this permission to a differently-named role).
        var ctoEmails = await db.Users
            .Where(u => u.IsActive && u.UserRoles.Any(ur =>
                ur.Role.RolePermissions.Any(rp => rp.Permission.Code == PermissionCodes.DeploymentsApproveProduction)))
            .Select(u => u.Email)
            .ToListAsync(cancellationToken);

        if (ctoEmails.Count == 0)
        {
            logger.LogWarning(
                "Production approval requested for application {ApplicationName} but no active user currently holds '{Permission}' to notify.",
                application.Name, PermissionCodes.DeploymentsApproveProduction);
            return;
        }

        var baseUrl = configuration["Portal:BaseUrl"];
        var reference = string.IsNullOrWhiteSpace(baseUrl)
            ? $"Approval reference: {approval.ApprovalToken}"
            : $"Review at: {baseUrl.TrimEnd('/')}/approvals/{approval.ApprovalToken}";

        var body =
            "A production deployment approval has been requested.\n\n" +
            $"Application: {application.Name}\n" +
            $"Commit: {promotion.CommitSha}\n" +
            $"Requested by: {currentUser.Username}\n\n" +
            $"{reference}\n\n" +
            "Log in to the DevOps Portal to review and approve or reject this request.";

        var sent = await emailSender.SendAsync(
            new EmailMessage(ctoEmails, $"Production approval requested: {application.Name}", body), cancellationToken);

        approval.EmailRecipients = string.Join(",", ctoEmails);
        if (sent)
            approval.EmailSentAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
    }

    private Guid RequireUserId() => currentUser.UserId ?? throw new ForbiddenException("Not authenticated.");

    private async Task EnsurePermissionAsync(Guid userId, string permissionCode, CancellationToken cancellationToken)
    {
        var (_, permissions) = await db.GetRolesAndPermissionsAsync(userId, cancellationToken);
        if (!permissions.Contains(permissionCode))
            throw new ForbiddenException($"Missing required permission '{permissionCode}'.");
    }

    /// <summary>Re-checks the current status immediately before mutating and returns
    /// false (rather than throwing) if it has already moved on — closes the common
    /// double-click/double-submit race for this specific transition. Provider-agnostic
    /// (works on EF Core InMemory, unlike ExecuteUpdateAsync) at the cost of not being a
    /// full DB-enforced atomic guarantee; the higher-severity duplicate-deployment race
    /// has a real DB-level backstop instead (see the partial unique index on Deployments).</summary>
    private async Task<bool> TryTransitionPromotionAsync(
        Guid promotionRequestId, ApprovalStatus newStatus, Guid userId, string? notes, CancellationToken cancellationToken)
    {
        var promotion = await db.PromotionRequests
            .FirstOrDefaultAsync(p => p.Id == promotionRequestId && p.Status == ApprovalStatus.PendingApproval, cancellationToken);
        if (promotion is null)
            return false;

        promotion.Status = newStatus;
        promotion.DecidedByUserId = userId;
        promotion.DecidedAt = DateTimeOffset.UtcNow;
        promotion.DecisionNotes = notes;
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    private async Task<bool> TryTransitionProductionApprovalAsync(
        Guid productionApprovalId, ApprovalStatus newStatus, Guid userId, string? notes, CancellationToken cancellationToken)
    {
        var approval = await db.ProductionApprovals
            .FirstOrDefaultAsync(a => a.Id == productionApprovalId && a.Status == ApprovalStatus.PendingApproval, cancellationToken);
        if (approval is null)
            return false;

        approval.Status = newStatus;
        approval.DecidedByUserId = userId;
        approval.DecidedAt = DateTimeOffset.UtcNow;
        approval.DecisionNotes = notes;
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    private async Task<ApplicationEnvironment?> LoadActiveEnvironmentAsync(
        Guid applicationId, Guid environmentDefinitionId, CancellationToken cancellationToken) =>
        await db.ApplicationEnvironments.FirstOrDefaultAsync(ae =>
            ae.ApplicationId == applicationId && ae.EnvironmentDefinitionId == environmentDefinitionId && ae.IsActive, cancellationToken);

    private async Task EnsureNoActiveDeploymentAsync(
        Guid applicationId, Guid environmentDefinitionId, string commitSha, CancellationToken cancellationToken)
    {
        var active = await db.Deployments
            .Where(d => d.ApplicationId == applicationId && d.EnvironmentDefinitionId == environmentDefinitionId &&
                (d.Status == DeploymentStatus.Pending || d.Status == DeploymentStatus.Queued || d.Status == DeploymentStatus.Running))
            .Select(d => d.CommitSha)
            .FirstOrDefaultAsync(cancellationToken);

        if (active is null)
            return;

        throw new ConflictException(active == commitSha
            ? $"This exact commit ({commitSha}) is already deploying to this environment."
            : "A different deployment is already in progress for this application and environment.");
    }

    private async Task SaveAndEnqueueAsync(Deployment deployment, CancellationToken cancellationToken)
    {
        db.Deployments.Add(deployment);
        await db.SaveChangesAsync(cancellationToken);
        jobQueue.Enqueue(deployment.Id);
    }

    private static string ValidateCommitSha(string? commitSha)
    {
        if (string.IsNullOrWhiteSpace(commitSha) || !CommitShaPattern().IsMatch(commitSha.Trim()))
            throw new ValidationException("CommitSha must be a 7-40 character hexadecimal git commit SHA.");
        return commitSha.Trim();
    }

    private Expression<Func<Deployment, DeploymentDto>> DeploymentProjection() => d => new DeploymentDto(
        d.Id, d.ApplicationId, d.Application.Name, d.EnvironmentDefinitionId, d.EnvironmentDefinition.Name,
        d.CommitSha, d.CommitMessage, d.CommitAuthor, d.Branch, d.ImageReference, d.VersionLabel,
        d.Status, d.IsRollback, d.RollbackOfDeploymentId, d.PromotionRequestId,
        d.RequestedByUserId, db.Users.Where(u => u.Id == d.RequestedByUserId).Select(u => u.Username).FirstOrDefault(),
        d.RequestedAt, d.StartedAt, d.CompletedAt, d.FailureReason, d.HealthCheckPassed, d.HealthCheckDetail);

    private Expression<Func<PromotionRequest, PromotionRequestDto>> PromotionProjection() => p => new PromotionRequestDto(
        p.Id, p.ApplicationId, p.Application.Name,
        p.FromEnvironmentDefinitionId, p.FromEnvironmentDefinition.Name,
        p.ToEnvironmentDefinitionId, p.ToEnvironmentDefinition.Name,
        p.SourceDeploymentId, p.CommitSha,
        p.Status, p.RequestedByUserId, db.Users.Where(u => u.Id == p.RequestedByUserId).Select(u => u.Username).FirstOrDefault(), p.RequestedAt,
        p.DecidedByUserId, p.DecidedAt, p.DecisionNotes,
        p.ToEnvironmentDefinition.IsProductionLike,
        p.ProductionApproval != null ? p.ProductionApproval.Status : (ApprovalStatus?)null,
        p.ProductionApproval != null ? p.ProductionApproval.EmailSentAt : null);

    [GeneratedRegex("^[0-9a-fA-F]{7,40}$")]
    private static partial Regex CommitShaPattern();
}
