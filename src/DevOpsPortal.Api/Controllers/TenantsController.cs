using DevOpsPortal.Application.Dtos.Tenants;
using DevOpsPortal.Application.Services;
using DevOpsPortal.Domain.Constants;
using DevOpsPortal.Infrastructure.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DevOpsPortal.Api.Controllers;

/// <summary>Platform-administrator-only — see PermissionCodes.TenantsView/TenantsManage.
/// No tenant's own role can ever be granted these codes (TenantService.CreateAsync
/// never assigns them to a tenant's provisioned roles).</summary>
[ApiController]
[Route("api/tenants")]
[Authorize]
[RequirePermission(PermissionCodes.TenantsView)]
public class TenantsController(ITenantService tenantService) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken cancellationToken) =>
        Ok(await tenantService.GetAllAsync(cancellationToken));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken) =>
        Ok(await tenantService.GetByIdAsync(id, cancellationToken));

    [HttpPost]
    [RequirePermission(PermissionCodes.TenantsManage)]
    public async Task<IActionResult> Create([FromBody] CreateTenantRequest request, CancellationToken cancellationToken)
    {
        var created = await tenantService.CreateAsync(request, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = created.Tenant.Id }, created);
    }

    [HttpPut("{id:guid}")]
    [RequirePermission(PermissionCodes.TenantsManage)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateTenantRequest request, CancellationToken cancellationToken) =>
        Ok(await tenantService.UpdateAsync(id, request, cancellationToken));
}
