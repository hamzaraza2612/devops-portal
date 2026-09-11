using DevOpsPortal.Application.Abstractions;
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
public class EnvironmentsController(
    IEnvironmentDefinitionService environmentDefinitionService,
    IEnvironmentInfrastructureService environmentInfrastructureService) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken cancellationToken) =>
        Ok(await environmentDefinitionService.GetAllAsync(cancellationToken));

    /// <summary>Only IsProductionLike, IsActive, and PrimaryTargetServerId are
    /// editable — Name and SortOrder are load-bearing pipeline structure, not
    /// admin-editable configuration. See IEnvironmentDefinitionService's doc
    /// comment.</summary>
    [HttpPut("{id:guid}")]
    [RequirePermission(PermissionCodes.EnvironmentsManage)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateEnvironmentDefinitionRequest request, CancellationToken cancellationToken) =>
        Ok(await environmentDefinitionService.UpdateAsync(id, request, cancellationToken));

    /// <summary>The Environment Infrastructure Dashboard's real-server-discovery
    /// endpoint — see IEnvironmentInfrastructureService. Permission/environment-access
    /// enforcement happens inside the service itself (same pattern as
    /// ContainerOperationsService), not via a controller-level attribute, so it
    /// is exercised identically regardless of what reaches this action.</summary>
    [HttpGet("{id:guid}/infrastructure")]
    public async Task<IActionResult> GetInfrastructure(Guid id, CancellationToken cancellationToken) =>
        Ok(await environmentInfrastructureService.GetInfrastructureAsync(id, cancellationToken));

    [HttpPost("{id:guid}/infrastructure/containers/{containerId}/start")]
    public async Task<IActionResult> StartContainer(Guid id, string containerId, CancellationToken cancellationToken) =>
        Ok(await environmentInfrastructureService.RunContainerActionAsync(id, containerId, RemoteContainerAction.Start, cancellationToken));

    [HttpPost("{id:guid}/infrastructure/containers/{containerId}/stop")]
    public async Task<IActionResult> StopContainer(Guid id, string containerId, CancellationToken cancellationToken) =>
        Ok(await environmentInfrastructureService.RunContainerActionAsync(id, containerId, RemoteContainerAction.Stop, cancellationToken));

    [HttpPost("{id:guid}/infrastructure/containers/{containerId}/restart")]
    public async Task<IActionResult> RestartContainer(Guid id, string containerId, CancellationToken cancellationToken) =>
        Ok(await environmentInfrastructureService.RunContainerActionAsync(id, containerId, RemoteContainerAction.Restart, cancellationToken));

    /// <summary>containerId is never trusted as-is — GetContainerLogsAsync
    /// re-validates it against a fresh discovery pass on this environment's
    /// target server before fetching anything (master requirement §4).</summary>
    [HttpGet("{id:guid}/infrastructure/containers/{containerId}/logs")]
    public async Task<IActionResult> GetContainerLogs(Guid id, string containerId, [FromQuery] int tailLines, CancellationToken cancellationToken) =>
        Ok(await environmentInfrastructureService.GetContainerLogsAsync(id, containerId, tailLines, cancellationToken));
}
