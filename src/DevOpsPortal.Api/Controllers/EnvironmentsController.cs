using DevOpsPortal.Application.Dtos.Environments;
using DevOpsPortal.Application.Services;
using DevOpsPortal.Domain.Constants;
using DevOpsPortal.Infrastructure.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DevOpsPortal.Api.Controllers;

[ApiController]
[Route("api/environments")]
[Authorize]
[RequirePermission(PermissionCodes.EnvironmentsView)]
public class EnvironmentsController(IEnvironmentDefinitionService environmentDefinitionService) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken cancellationToken) =>
        Ok(await environmentDefinitionService.GetAllAsync(cancellationToken));

    /// <summary>Only IsProductionLike and IsActive are editable — Name and
    /// SortOrder are load-bearing pipeline structure, not admin-editable
    /// configuration. See IEnvironmentDefinitionService's doc comment.</summary>
    [HttpPut("{id:guid}")]
    [RequirePermission(PermissionCodes.EnvironmentsManage)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateEnvironmentDefinitionRequest request, CancellationToken cancellationToken) =>
        Ok(await environmentDefinitionService.UpdateAsync(id, request, cancellationToken));
}
