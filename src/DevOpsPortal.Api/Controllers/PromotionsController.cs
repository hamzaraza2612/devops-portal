using DevOpsPortal.Application.Dtos.Deployments;
using DevOpsPortal.Application.Services;
using DevOpsPortal.Domain.Constants;
using DevOpsPortal.Infrastructure.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DevOpsPortal.Api.Controllers;

/// <summary>
/// Approval and deploy are always two separate calls here — approving never
/// deploys anything by itself (master requirements §8/§10).
/// </summary>
[ApiController]
[Route("api/promotions")]
[Authorize]
public class PromotionsController(IDeploymentService deploymentService) : ControllerBase
{
    [HttpGet]
    [RequirePermission(PermissionCodes.DeploymentsView)]
    public async Task<IActionResult> ListPending(
        [FromQuery] Guid? applicationId, [FromQuery] Guid? toEnvironmentDefinitionId,
        [FromQuery] bool includeApprovedAwaitingDeploy, CancellationToken cancellationToken) =>
        Ok(await deploymentService.ListPendingPromotionsAsync(applicationId, toEnvironmentDefinitionId, includeApprovedAwaitingDeploy, cancellationToken));

    [HttpGet("{id:guid}")]
    [RequirePermission(PermissionCodes.DeploymentsView)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken) =>
        Ok(await deploymentService.GetPromotionAsync(id, cancellationToken));

    /// <summary>Permission is environment-dependent (QA/UAT/Production have different
    /// required permissions) and is therefore enforced inside the service, not by a
    /// static attribute here — see IDeploymentService.ApprovePromotionAsync.</summary>
    [HttpPost("{id:guid}/approve")]
    public async Task<IActionResult> Approve(Guid id, [FromBody] DecidePromotionRequest request, CancellationToken cancellationToken) =>
        Ok(await deploymentService.ApprovePromotionAsync(id, request, cancellationToken));

    [HttpPost("{id:guid}/reject")]
    public async Task<IActionResult> Reject(Guid id, [FromBody] DecidePromotionRequest request, CancellationToken cancellationToken) =>
        Ok(await deploymentService.RejectPromotionAsync(id, request, cancellationToken));

    [HttpPost("{id:guid}/deploy")]
    public async Task<IActionResult> Deploy(Guid id, CancellationToken cancellationToken) =>
        Ok(await deploymentService.DeployApprovedPromotionAsync(id, cancellationToken));

    /// <summary>The one deliberately unauthenticated endpoint on this controller —
    /// lets an approval-requested notification email deep-link to a read-only
    /// preview without requiring the recipient to log in first. Never a path to
    /// approve/reject anything (master requirements §4) — see
    /// ApprovalTokenHelper's and IDeploymentService.GetPromotionPreviewByTokenAsync's
    /// doc comments for the full rationale.</summary>
    [HttpGet("by-token/{token}")]
    [AllowAnonymous]
    public async Task<IActionResult> GetPreviewByToken(string token, CancellationToken cancellationToken) =>
        Ok(await deploymentService.GetPromotionPreviewByTokenAsync(token, cancellationToken));

    [HttpGet("production-approvals/by-token/{token}")]
    [AllowAnonymous]
    public async Task<IActionResult> GetProductionApprovalPreviewByToken(string token, CancellationToken cancellationToken) =>
        Ok(await deploymentService.GetProductionApprovalPreviewByTokenAsync(token, cancellationToken));
}
