using DevOpsPortal.Application.Abstractions;
using DevOpsPortal.Application.Common;
using DevOpsPortal.Domain.Constants;
using DevOpsPortal.Domain.Entities;
using DevOpsPortal.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace DevOpsPortal.Application.Services;

public class NotificationService(
    IAppDbContext db,
    IEnumerable<INotificationProvider> providers,
    IAuditService auditService,
    IConfiguration configuration,
    ILogger<NotificationService> logger) : INotificationService
{
    public async Task NotifyPromotionApprovalRequestedAsync(
        PromotionRequest promotion, string permissionCode, string rawApprovalToken, CancellationToken cancellationToken = default)
    {
        var recipients = await ResolveEmailsByPermissionAsync(permissionCode, cancellationToken);
        if (recipients.Count == 0)
        {
            logger.LogWarning(
                "Promotion {PromotionId} approval requested but no active user currently holds '{Permission}' to notify.",
                promotion.Id, permissionCode);
            return;
        }

        var requestedByUsername = await ResolveUsernameAsync(promotion.RequestedByUserId, cancellationToken);
        var link = BuildLink("promotions", promotion.Id, rawApprovalToken);
        var body =
            "A promotion approval has been requested.\n\n" +
            $"Application: {promotion.Application.Name}\n" +
            $"{promotion.FromEnvironmentDefinition.Name} -> {promotion.ToEnvironmentDefinition.Name}\n" +
            $"Commit: {promotion.CommitSha}\n" +
            $"Requested by: {requestedByUsername ?? "unknown"}\n\n" +
            $"{link}\n\n" +
            "Log in to the DevOps Portal to review and approve or reject this request.";

        var sent = await BroadcastAsync(recipients, $"Approval requested: {promotion.Application.Name} -> {promotion.ToEnvironmentDefinition.Name}", body, cancellationToken);

        promotion.NotificationRecipients = string.Join(",", recipients);
        if (sent)
            promotion.NotifiedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        await auditService.LogAsync("notification.promotion_approval_requested", sent ? AuditResult.Success : AuditResult.Failure,
            "PromotionRequest", promotion.Id.ToString(), details: $"{recipients.Count} recipient(s)", cancellationToken: cancellationToken);
    }

    public async Task NotifyProductionApprovalRequestedAsync(
        PromotionRequest promotion, ProductionApproval approval, string rawApprovalToken, CancellationToken cancellationToken = default)
    {
        // Permission-based, not role-name-based — a tenant could grant
        // deployments.approve.production to a differently-named or multiple
        // roles without any code change (see class doc comment).
        var recipients = await ResolveEmailsByPermissionAsync(PermissionCodes.DeploymentsApproveProduction, cancellationToken);
        if (recipients.Count == 0)
        {
            logger.LogWarning(
                "Production approval requested for application {ApplicationName} but no active user currently holds '{Permission}' to notify.",
                promotion.Application.Name, PermissionCodes.DeploymentsApproveProduction);
            return;
        }

        var requestedByUsername = await ResolveUsernameAsync(promotion.RequestedByUserId, cancellationToken);
        var link = BuildLink("production-approvals", approval.Id, rawApprovalToken);
        var body =
            "A production deployment approval has been requested.\n\n" +
            $"Application: {promotion.Application.Name}\n" +
            $"Commit: {promotion.CommitSha}\n" +
            $"Requested by: {requestedByUsername ?? "unknown"}\n\n" +
            $"{link}\n\n" +
            "Log in to the DevOps Portal to review and approve or reject this request.";

        var sent = await BroadcastAsync(recipients, $"Production approval requested: {promotion.Application.Name}", body, cancellationToken);

        approval.NotificationRecipients = string.Join(",", recipients);
        if (sent)
            approval.NotifiedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        await auditService.LogAsync("notification.production_approval_requested", sent ? AuditResult.Success : AuditResult.Failure,
            "ProductionApproval", approval.Id.ToString(), details: $"{recipients.Count} recipient(s)", cancellationToken: cancellationToken);
    }

    public async Task NotifyDeploymentStartedAsync(Deployment deployment, CancellationToken cancellationToken = default)
    {
        var recipient = await db.Users.Where(u => u.Id == deployment.RequestedByUserId).Select(u => u.Email).FirstOrDefaultAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(recipient))
            return;

        var verb = deployment.IsRollback ? "Rollback" : "Deployment";
        var body =
            $"{verb} started.\n\n" +
            $"Application: {deployment.Application.Name}\n" +
            $"Environment: {deployment.EnvironmentDefinition.Name}\n" +
            $"Commit: {deployment.CommitSha}\n";

        var sent = await BroadcastAsync([recipient], $"{verb} started: {deployment.Application.Name}/{deployment.EnvironmentDefinition.Name}", body, cancellationToken);

        await auditService.LogAsync("notification.deployment_started", sent ? AuditResult.Success : AuditResult.Failure,
            "Deployment", deployment.Id.ToString(), details: deployment.IsRollback ? "rollback" : "deployment", cancellationToken: cancellationToken);
    }

    public async Task NotifyDeploymentOutcomeAsync(Deployment deployment, CancellationToken cancellationToken = default)
    {
        var recipient = await db.Users.Where(u => u.Id == deployment.RequestedByUserId).Select(u => u.Email).FirstOrDefaultAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(recipient))
            return;

        var succeeded = deployment.Status == DeploymentStatus.Succeeded;
        var verb = deployment.IsRollback ? "Rollback" : "Deployment";
        var outcome = succeeded ? "succeeded" : "failed";

        var body =
            $"{verb} {outcome}.\n\n" +
            $"Application: {deployment.Application.Name}\n" +
            $"Environment: {deployment.EnvironmentDefinition.Name}\n" +
            $"Commit: {deployment.CommitSha}\n" +
            (succeeded ? string.Empty : $"Reason: {deployment.FailureReason}\n");

        var sent = await BroadcastAsync(
            [recipient], $"{verb} {outcome}: {deployment.Application.Name}/{deployment.EnvironmentDefinition.Name}", body, cancellationToken);

        await auditService.LogAsync("notification.deployment_outcome", sent ? AuditResult.Success : AuditResult.Failure,
            "Deployment", deployment.Id.ToString(), details: $"{(deployment.IsRollback ? "rollback" : "deployment")} {outcome}",
            cancellationToken: cancellationToken);
    }

    // ------------------------------------------------------------- internals

    private async Task<IReadOnlyList<string>> ResolveEmailsByPermissionAsync(string permissionCode, CancellationToken cancellationToken) =>
        await db.Users
            .Where(u => u.IsActive && u.UserRoles.Any(ur => ur.Role.RolePermissions.Any(rp => rp.Permission.Code == permissionCode)))
            .Select(u => u.Email)
            .Distinct()
            .ToListAsync(cancellationToken);

    private async Task<string?> ResolveUsernameAsync(Guid userId, CancellationToken cancellationToken) =>
        await db.Users.Where(u => u.Id == userId).Select(u => u.Username).FirstOrDefaultAsync(cancellationToken);

    /// <summary>Sends via every registered provider, never letting one provider's
    /// failure stop the others. Returns true if at least one provider reported
    /// success.</summary>
    private async Task<bool> BroadcastAsync(IReadOnlyList<string> recipients, string subject, string body, CancellationToken cancellationToken)
    {
        var anySucceeded = false;
        foreach (var provider in providers)
        {
            try
            {
                if (await provider.SendAsync(new NotificationMessage(recipients, subject, body), cancellationToken))
                    anySucceeded = true;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Notification provider {Provider} failed to send '{Subject}'", provider.GetType().Name, subject);
            }
        }
        return anySucceeded;
    }

    /// <summary>The raw token always appears in the notification body, with or
    /// without a configured Portal:BaseUrl — losing it entirely when BaseUrl is
    /// unset would make the notification useless (no way to identify which
    /// approval it's even about) and was the actual bug in this method's first
    /// draft. Never persisted anywhere — see ApprovalTokenHelper's doc comment.</summary>
    private string BuildLink(string relativePathSegment, Guid entityId, string rawToken)
    {
        var baseUrl = configuration["Portal:BaseUrl"];
        return string.IsNullOrWhiteSpace(baseUrl)
            ? $"Approval reference: {entityId} / token {rawToken} (ask an administrator for the portal URL to review this using it)."
            : $"Review at: {baseUrl.TrimEnd('/')}/{relativePathSegment}/{entityId}?token={rawToken}";
    }
}
