using DevOpsPortal.Application.Dtos.Repositories;
using DevOpsPortal.Application.Services;
using DevOpsPortal.Domain.Constants;
using DevOpsPortal.Infrastructure.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DevOpsPortal.Api.Controllers;

[ApiController]
[Route("api/repositories")]
[Authorize]
public class RepositoriesController(IRepositoryService repositoryService) : ControllerBase
{
    [HttpGet]
    [RequirePermission(PermissionCodes.RepositoriesView)]
    public async Task<IActionResult> GetAll(CancellationToken cancellationToken) =>
        Ok(await repositoryService.GetAllAsync(cancellationToken));

    [HttpGet("{id:guid}")]
    [RequirePermission(PermissionCodes.RepositoriesView)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken) =>
        Ok(await repositoryService.GetByIdAsync(id, cancellationToken));

    [HttpPost]
    [RequirePermission(PermissionCodes.RepositoriesManage)]
    public async Task<IActionResult> Create([FromBody] CreateRepositoryRequest request, CancellationToken cancellationToken)
    {
        var created = await repositoryService.CreateAsync(request, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    [HttpPut("{id:guid}")]
    [RequirePermission(PermissionCodes.RepositoriesManage)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateRepositoryRequest request, CancellationToken cancellationToken) =>
        Ok(await repositoryService.UpdateAsync(id, request, cancellationToken));

    [HttpPut("{id:guid}/access-token")]
    [RequirePermission(PermissionCodes.RepositoriesManage)]
    public async Task<IActionResult> SetAccessToken(Guid id, [FromBody] SetRepositoryAccessTokenRequest request, CancellationToken cancellationToken) =>
        Ok(await repositoryService.SetAccessTokenAsync(id, request, cancellationToken));

    [HttpPost("{id:guid}/test-connection")]
    [RequirePermission(PermissionCodes.RepositoriesManage)]
    public async Task<IActionResult> TestConnection(Guid id, CancellationToken cancellationToken) =>
        Ok(await repositoryService.TestConnectionAsync(id, cancellationToken));
}
