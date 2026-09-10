using DevOpsPortal.Application.Dtos.Secrets;
using DevOpsPortal.Application.Services;
using DevOpsPortal.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DevOpsPortal.Api.Controllers;

/// <summary>Permission checks (secrets.view/secrets.manage) are enforced inside
/// ISecretReferenceService, not via a static [RequirePermission] attribute here
/// — same pattern as containers/builds — so the same authorization logic is
/// exercised whether or not a request reaches this controller. No action on
/// this controller ever returns a secret value; there is no such method on
/// ISecretReferenceService for a controller to call in the first place.</summary>
[ApiController]
[Route("api/secrets")]
[Authorize]
public class SecretsController(ISecretReferenceService secretReferenceService) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] Guid? applicationId, [FromQuery] Guid? environmentDefinitionId, [FromQuery] SecretCategory? category,
        CancellationToken cancellationToken) =>
        Ok(await secretReferenceService.ListAsync(applicationId, environmentDefinitionId, category, cancellationToken));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken) =>
        Ok(await secretReferenceService.GetAsync(id, cancellationToken));

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateSecretReferenceRequest request, CancellationToken cancellationToken)
    {
        var created = await secretReferenceService.CreateAsync(request, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    /// <summary>Value is optional — omit it to change only Description/IsActive;
    /// supply it to rotate the secret's value in place.</summary>
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateSecretReferenceRequest request, CancellationToken cancellationToken) =>
        Ok(await secretReferenceService.UpdateAsync(id, request, cancellationToken));

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await secretReferenceService.DeleteAsync(id, cancellationToken);
        return NoContent();
    }
}
