using DevOpsPortal.Application.Dtos.TargetServers;
using DevOpsPortal.Application.Services;
using DevOpsPortal.Domain.Constants;
using DevOpsPortal.Infrastructure.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DevOpsPortal.Api.Controllers;

[ApiController]
[Route("api/target-servers")]
[Authorize]
public class TargetServersController(ITargetServerService targetServerService) : ControllerBase
{
    [HttpGet]
    [RequirePermission(PermissionCodes.TargetServersView)]
    public async Task<IActionResult> GetAll(CancellationToken cancellationToken) =>
        Ok(await targetServerService.GetAllAsync(cancellationToken));

    [HttpGet("{id:guid}")]
    [RequirePermission(PermissionCodes.TargetServersView)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken) =>
        Ok(await targetServerService.GetByIdAsync(id, cancellationToken));

    [HttpPost]
    [RequirePermission(PermissionCodes.TargetServersManage)]
    public async Task<IActionResult> Create([FromBody] CreateTargetServerRequest request, CancellationToken cancellationToken)
    {
        var created = await targetServerService.CreateAsync(request, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    [HttpPut("{id:guid}")]
    [RequirePermission(PermissionCodes.TargetServersManage)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateTargetServerRequest request, CancellationToken cancellationToken) =>
        Ok(await targetServerService.UpdateAsync(id, request, cancellationToken));

    /// <summary>Blocked (409) while any ApplicationEnvironment references this
    /// server — set IsActive: false via PUT instead.</summary>
    [HttpDelete("{id:guid}")]
    [RequirePermission(PermissionCodes.TargetServersManage)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await targetServerService.DeleteAsync(id, cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/allowed-roots")]
    [RequirePermission(PermissionCodes.TargetServersManage)]
    public async Task<IActionResult> AddAllowedRoot(Guid id, [FromBody] CreateAllowedDeploymentRootRequest request, CancellationToken cancellationToken)
    {
        var created = await targetServerService.AddAllowedRootAsync(id, request, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id }, created);
    }

    [HttpPut("{id:guid}/allowed-roots/{rootId:guid}")]
    [RequirePermission(PermissionCodes.TargetServersManage)]
    public async Task<IActionResult> UpdateAllowedRoot(
        Guid id, Guid rootId, [FromBody] UpdateAllowedDeploymentRootRequest request, CancellationToken cancellationToken) =>
        Ok(await targetServerService.UpdateAllowedRootAsync(id, rootId, request, cancellationToken));

    [HttpPut("{id:guid}/ssh-credential")]
    [RequirePermission(PermissionCodes.TargetServersManage)]
    public async Task<IActionResult> SetSshCredential(Guid id, [FromBody] SetSshCredentialRequest request, CancellationToken cancellationToken) =>
        Ok(await targetServerService.SetSshCredentialAsync(id, request, cancellationToken));

    [HttpPut("{id:guid}/ssh-passphrase")]
    [RequirePermission(PermissionCodes.TargetServersManage)]
    public async Task<IActionResult> SetSshPassphrase(Guid id, [FromBody] SetSshPassphraseRequest request, CancellationToken cancellationToken) =>
        Ok(await targetServerService.SetSshPassphraseAsync(id, request, cancellationToken));

    [HttpPost("{id:guid}/test-connection")]
    [RequirePermission(PermissionCodes.TargetServersManage)]
    public async Task<IActionResult> TestConnection(Guid id, CancellationToken cancellationToken) =>
        Ok(await targetServerService.TestConnectionAsync(id, cancellationToken));
}
