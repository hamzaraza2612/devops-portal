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

    /// <summary>Any application referencing this repository just has its link
    /// cleared, never blocked (see RepositoryService.DeleteAsync doc comment).</summary>
    [HttpDelete("{id:guid}")]
    [RequirePermission(PermissionCodes.RepositoriesManage)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await repositoryService.DeleteAsync(id, cancellationToken);
        return NoContent();
    }

    [HttpPut("{id:guid}/access-token")]
    [RequirePermission(PermissionCodes.RepositoriesManage)]
    public async Task<IActionResult> SetAccessToken(Guid id, [FromBody] SetRepositoryAccessTokenRequest request, CancellationToken cancellationToken) =>
        Ok(await repositoryService.SetAccessTokenAsync(id, request, cancellationToken));

    [HttpPost("{id:guid}/test-connection")]
    [RequirePermission(PermissionCodes.RepositoriesManage)]
    public async Task<IActionResult> TestConnection(Guid id, CancellationToken cancellationToken) =>
        Ok(await repositoryService.TestConnectionAsync(id, cancellationToken));

    /// <summary>"Discover applications from this repository" — a monorepo-style
    /// scan (master requirements: the source folder should come from the repo
    /// itself, never typed in by hand). Gated the same as every other
    /// repository-inspection action (RepositoriesManage), since it's an admin
    /// action that reaches out to GitLab.</summary>
    [HttpGet("{id:guid}/discover-applications")]
    [RequirePermission(PermissionCodes.RepositoriesManage)]
    public async Task<IActionResult> DiscoverApplications(Guid id, [FromQuery] string? branch, CancellationToken cancellationToken) =>
        Ok(await repositoryService.DiscoverApplicationsAsync(id, branch, cancellationToken));

    /// <summary>Read-only — gated the same as GetAll/GetById (RepositoriesView),
    /// not RepositoriesManage, since picking a branch to deploy is something any
    /// user configuring an application-environment they can already manage needs
    /// to do, independent of whether they can also edit Repository entities.</summary>
    [HttpGet("{id:guid}/branches")]
    [RequirePermission(PermissionCodes.RepositoriesView)]
    public async Task<IActionResult> GetBranches(Guid id, CancellationToken cancellationToken) =>
        Ok(await repositoryService.GetBranchesAsync(id, cancellationToken));
}
