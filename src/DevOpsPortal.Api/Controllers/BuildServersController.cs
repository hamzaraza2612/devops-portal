using DevOpsPortal.Application.Dtos.Builds;
using DevOpsPortal.Application.Services;
using DevOpsPortal.Domain.Constants;
using DevOpsPortal.Infrastructure.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DevOpsPortal.Api.Controllers;

[ApiController]
[Route("api/build-servers")]
[Authorize]
public class BuildServersController(IBuildServerService buildServerService) : ControllerBase
{
    [HttpGet]
    [RequirePermission(PermissionCodes.BuildServersView)]
    public async Task<IActionResult> GetAll(CancellationToken cancellationToken) =>
        Ok(await buildServerService.GetAllAsync(cancellationToken));

    [HttpGet("{id:guid}")]
    [RequirePermission(PermissionCodes.BuildServersView)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken) =>
        Ok(await buildServerService.GetByIdAsync(id, cancellationToken));

    [HttpPost]
    [RequirePermission(PermissionCodes.BuildServersManage)]
    public async Task<IActionResult> Create([FromBody] CreateBuildServerRequest request, CancellationToken cancellationToken)
    {
        var created = await buildServerService.CreateAsync(request, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    [HttpPut("{id:guid}")]
    [RequirePermission(PermissionCodes.BuildServersManage)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateBuildServerRequest request, CancellationToken cancellationToken) =>
        Ok(await buildServerService.UpdateAsync(id, request, cancellationToken));
}
