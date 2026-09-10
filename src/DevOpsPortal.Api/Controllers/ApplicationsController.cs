using DevOpsPortal.Application.Dtos.Applications;
using DevOpsPortal.Application.Services;
using DevOpsPortal.Domain.Constants;
using DevOpsPortal.Infrastructure.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DevOpsPortal.Api.Controllers;

[ApiController]
[Route("api/applications")]
[Authorize]
public class ApplicationsController(IApplicationService applicationService, IApplicationEnvironmentService environmentService) : ControllerBase
{
    [HttpGet]
    [RequirePermission(PermissionCodes.ApplicationsView)]
    public async Task<IActionResult> GetAll(CancellationToken cancellationToken) =>
        Ok(await applicationService.GetAllAsync(cancellationToken));

    [HttpGet("{id:guid}")]
    [RequirePermission(PermissionCodes.ApplicationsView)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken) =>
        Ok(await applicationService.GetByIdAsync(id, cancellationToken));

    [HttpPost]
    [RequirePermission(PermissionCodes.ApplicationsManage)]
    public async Task<IActionResult> Create([FromBody] CreateApplicationRequest request, CancellationToken cancellationToken)
    {
        var created = await applicationService.CreateAsync(request, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    [HttpPut("{id:guid}")]
    [RequirePermission(PermissionCodes.ApplicationsManage)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateApplicationRequest request, CancellationToken cancellationToken) =>
        Ok(await applicationService.UpdateAsync(id, request, cancellationToken));

    [HttpGet("{id:guid}/environments")]
    [RequirePermission(PermissionCodes.ApplicationsView)]
    public async Task<IActionResult> GetEnvironments(Guid id, CancellationToken cancellationToken) =>
        Ok(await environmentService.GetForApplicationAsync(id, cancellationToken));

    [HttpGet("{id:guid}/environments/{environmentDefinitionId:guid}")]
    [RequirePermission(PermissionCodes.ApplicationsView)]
    public async Task<IActionResult> GetEnvironment(Guid id, Guid environmentDefinitionId, CancellationToken cancellationToken) =>
        Ok(await environmentService.GetAsync(id, environmentDefinitionId, cancellationToken));

    [HttpPut("{id:guid}/environments/{environmentDefinitionId:guid}")]
    [RequirePermission(PermissionCodes.ApplicationsManage)]
    public async Task<IActionResult> UpsertEnvironment(
        Guid id, Guid environmentDefinitionId, [FromBody] UpsertApplicationEnvironmentRequest request, CancellationToken cancellationToken) =>
        Ok(await environmentService.UpsertAsync(id, environmentDefinitionId, request, cancellationToken));
}
