using DevOpsPortal.Application.Services;
using DevOpsPortal.Domain.Constants;
using DevOpsPortal.Infrastructure.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DevOpsPortal.Api.Controllers;

[ApiController]
[Route("api/roles")]
[Authorize]
[RequirePermission(PermissionCodes.RolesView)]
public class RolesController(IRoleService roleService) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken cancellationToken) =>
        Ok(await roleService.GetAllRolesAsync(cancellationToken));

    [HttpGet("permissions")]
    public async Task<IActionResult> GetAllPermissions(CancellationToken cancellationToken) =>
        Ok(await roleService.GetAllPermissionsAsync(cancellationToken));
}
