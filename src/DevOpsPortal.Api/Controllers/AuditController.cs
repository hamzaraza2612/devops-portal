using DevOpsPortal.Application.Dtos.Audit;
using DevOpsPortal.Application.Services;
using DevOpsPortal.Domain.Constants;
using DevOpsPortal.Infrastructure.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DevOpsPortal.Api.Controllers;

[ApiController]
[Route("api/audit")]
[Authorize]
[RequirePermission(PermissionCodes.AuditView)]
public class AuditController(IAuditService auditService) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Query([FromQuery] AuditQuery query, CancellationToken cancellationToken) =>
        Ok(await auditService.QueryAsync(query, cancellationToken));
}
