using DevOpsPortal.Application.Services;
using DevOpsPortal.Domain.Constants;
using DevOpsPortal.Domain.Enums;
using DevOpsPortal.Infrastructure.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DevOpsPortal.Api.Controllers;

[ApiController]
[Route("api/deployments")]
[Authorize]
[RequirePermission(PermissionCodes.DeploymentsView)]
public class DeploymentsController(IDeploymentService deploymentService) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] Guid? applicationId, [FromQuery] Guid? environmentDefinitionId, [FromQuery] DeploymentStatus? status,
        CancellationToken cancellationToken) =>
        Ok(await deploymentService.ListAsync(applicationId, environmentDefinitionId, status, cancellationToken));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken) =>
        Ok(await deploymentService.GetAsync(id, cancellationToken));

    [HttpGet("{id:guid}/logs")]
    public async Task<IActionResult> GetLogs(Guid id, CancellationToken cancellationToken) =>
        Ok(await deploymentService.GetLogsAsync(id, cancellationToken));
}
