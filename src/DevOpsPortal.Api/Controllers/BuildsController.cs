using DevOpsPortal.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DevOpsPortal.Api.Controllers;

/// <summary>Flat, by-id access to build requests — permission (builds.view) is
/// enforced inside IBuildService, same pattern as DeploymentsController's
/// by-id routes and the container endpoints on ApplicationsController.</summary>
[ApiController]
[Route("api/builds")]
[Authorize]
public class BuildsController(IBuildService buildService) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] Guid? applicationId, CancellationToken cancellationToken) =>
        Ok(await buildService.ListAsync(applicationId, cancellationToken));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken) =>
        Ok(await buildService.GetAsync(id, cancellationToken));

    [HttpGet("{id:guid}/logs")]
    public async Task<IActionResult> GetLogs(Guid id, CancellationToken cancellationToken) =>
        Ok(await buildService.GetLogAsync(id, cancellationToken));
}
