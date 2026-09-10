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
}
