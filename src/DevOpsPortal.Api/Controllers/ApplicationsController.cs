using DevOpsPortal.Application.Dtos.Applications;
using DevOpsPortal.Application.Dtos.Builds;
using DevOpsPortal.Application.Dtos.Containers;
using DevOpsPortal.Application.Dtos.Deployments;
using DevOpsPortal.Application.Services;
using DevOpsPortal.Domain.Constants;
using DevOpsPortal.Infrastructure.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DevOpsPortal.Api.Controllers;

[ApiController]
[Route("api/applications")]
[Authorize]
public class ApplicationsController(
    IApplicationService applicationService,
    IApplicationEnvironmentService environmentService,
    IDeploymentService deploymentService,
    IBuildConfigurationService buildConfigurationService,
    IContainerOperationsService containerOperationsService,
    IBuildService buildService) : ControllerBase
{
    [HttpGet]
    [RequirePermission(PermissionCodes.ApplicationsView)]
    public async Task<IActionResult> GetAll(CancellationToken cancellationToken) =>
        Ok(await applicationService.GetAllAsync(cancellationToken));

    [HttpGet("{id:guid}")]
    [RequirePermission(PermissionCodes.ApplicationsView)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken) =>
        Ok(await applicationService.GetByIdAsync(id, cancellationToken));

    [HttpPost]
    [RequirePermission(PermissionCodes.ApplicationsManage)]
    public async Task<IActionResult> Create([FromBody] CreateApplicationRequest request, CancellationToken cancellationToken)
    {
        var created = await applicationService.CreateAsync(request, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    [HttpPut("{id:guid}")]
    [RequirePermission(PermissionCodes.ApplicationsManage)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateApplicationRequest request, CancellationToken cancellationToken) =>
        Ok(await applicationService.UpdateAsync(id, request, cancellationToken));

    /// <summary>Blocked (409) while any Deployment exists for this application —
    /// deactivate it (PUT with IsActive: false) instead to preserve history.</summary>
    [HttpDelete("{id:guid}")]
    [RequirePermission(PermissionCodes.ApplicationsManage)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await applicationService.DeleteAsync(id, cancellationToken);
        return NoContent();
    }

    [HttpGet("{id:guid}/environments")]
    [RequirePermission(PermissionCodes.ApplicationsView)]
    public async Task<IActionResult> GetEnvironments(Guid id, CancellationToken cancellationToken) =>
        Ok(await environmentService.GetForApplicationAsync(id, cancellationToken));

    [HttpGet("{id:guid}/environments/{environmentDefinitionId:guid}")]
    [RequirePermission(PermissionCodes.ApplicationsView)]
    public async Task<IActionResult> GetEnvironment(Guid id, Guid environmentDefinitionId, CancellationToken cancellationToken) =>
        Ok(await environmentService.GetAsync(id, environmentDefinitionId, cancellationToken));

    [HttpPut("{id:guid}/environments/{environmentDefinitionId:guid}")]
    [RequirePermission(PermissionCodes.ApplicationsManage)]
    public async Task<IActionResult> UpsertEnvironment(
        Guid id, Guid environmentDefinitionId, [FromBody] UpsertApplicationEnvironmentRequest request, CancellationToken cancellationToken) =>
        Ok(await environmentService.UpsertAsync(id, environmentDefinitionId, request, cancellationToken));

    /// <summary>Blocked (409) while any Deployment references this row —
    /// upsert with IsActive: false instead to preserve deployment history.</summary>
    [HttpDelete("{id:guid}/environments/{environmentDefinitionId:guid}")]
    [RequirePermission(PermissionCodes.ApplicationsManage)]
    public async Task<IActionResult> DeleteEnvironment(Guid id, Guid environmentDefinitionId, CancellationToken cancellationToken)
    {
        await environmentService.DeleteAsync(id, environmentDefinitionId, cancellationToken);
        return NoContent();
    }

    [HttpGet("{id:guid}/environments/{environmentDefinitionId:guid}/commits/latest")]
    [RequirePermission(PermissionCodes.ApplicationsView)]
    public async Task<IActionResult> GetLatestCommit(Guid id, Guid environmentDefinitionId, CancellationToken cancellationToken) =>
        Ok(await environmentService.GetLatestCommitAsync(id, environmentDefinitionId, cancellationToken));

    [HttpGet("{id:guid}/environments/{environmentDefinitionId:guid}/commits/recent")]
    [RequirePermission(PermissionCodes.ApplicationsView)]
    public async Task<IActionResult> GetRecentCommits(
        Guid id, Guid environmentDefinitionId, [FromQuery] int count, CancellationToken cancellationToken) =>
        Ok(await environmentService.GetRecentCommitsAsync(id, environmentDefinitionId, count <= 0 ? 20 : count, cancellationToken));

    [HttpGet("{id:guid}/environments/{environmentDefinitionId:guid}/status")]
    [RequirePermission(PermissionCodes.DeploymentsView)]
    public async Task<IActionResult> GetStatus(Guid id, Guid environmentDefinitionId, CancellationToken cancellationToken) =>
        Ok(await deploymentService.GetStatusAsync(id, environmentDefinitionId, cancellationToken));

    /// <summary>The only direct-deploy entry point — DEV has no promotion/approval gate.
    /// Permission (deployments.deploy.dev) is enforced inside the service.</summary>
    [HttpPost("{id:guid}/deployments/dev")]
    public async Task<IActionResult> DeployToDev(Guid id, [FromBody] CreateDevDeploymentRequest request, CancellationToken cancellationToken) =>
        Ok(await deploymentService.DeployToDevAsync(id, request, cancellationToken));

    /// <summary>Permission is environment-dependent (promote.qa/uat/production) and is
    /// therefore enforced inside the service, not by a static attribute here.</summary>
    [HttpPost("{id:guid}/environments/{environmentDefinitionId:guid}/promotions")]
    public async Task<IActionResult> RequestPromotion(
        Guid id, Guid environmentDefinitionId, [FromBody] CreatePromotionRequest request, CancellationToken cancellationToken) =>
        Ok(await deploymentService.RequestPromotionAsync(id, environmentDefinitionId, request, cancellationToken));

    [HttpPost("{id:guid}/environments/{environmentDefinitionId:guid}/rollback")]
    public async Task<IActionResult> Rollback(
        Guid id, Guid environmentDefinitionId, [FromBody] RollbackRequest request, CancellationToken cancellationToken) =>
        Ok(await deploymentService.RollbackAsync(id, environmentDefinitionId, request, cancellationToken));

    [HttpGet("{id:guid}/build-configuration")]
    [RequirePermission(PermissionCodes.ApplicationsView)]
    public async Task<IActionResult> GetBuildConfiguration(Guid id, CancellationToken cancellationToken) =>
        Ok(await buildConfigurationService.GetAsync(id, cancellationToken));

    [HttpPut("{id:guid}/build-configuration")]
    [RequirePermission(PermissionCodes.ApplicationsManage)]
    public async Task<IActionResult> UpsertBuildConfiguration(
        Guid id, [FromBody] UpsertBuildConfigurationRequest request, CancellationToken cancellationToken) =>
        Ok(await buildConfigurationService.UpsertAsync(id, request, cancellationToken));

    /// <summary>Live container status — permission (containers.view) is enforced
    /// inside the service, same pattern as the deployment endpoints above.</summary>
    [HttpGet("{id:guid}/environments/{environmentDefinitionId:guid}/containers")]
    public async Task<IActionResult> GetContainerStatus(Guid id, Guid environmentDefinitionId, CancellationToken cancellationToken) =>
        Ok(await containerOperationsService.GetStatusAsync(id, environmentDefinitionId, cancellationToken));

    /// <summary>Recent `docker logs --tail N` output for one container of this
    /// application environment. containerName is re-validated server-side
    /// against this environment's own live `compose ps` discovery (never
    /// trusted as-is); permission (containers.view) and environment access are
    /// enforced inside the service, same pattern as GetContainerStatus.</summary>
    [HttpGet("{id:guid}/environments/{environmentDefinitionId:guid}/containers/logs")]
    public async Task<IActionResult> GetContainerLogs(
        Guid id, Guid environmentDefinitionId, [FromQuery] string containerName, [FromQuery] int tailLines, CancellationToken cancellationToken) =>
        Ok(await containerOperationsService.GetLogsAsync(id, environmentDefinitionId, containerName, tailLines, cancellationToken));

    [HttpPost("{id:guid}/environments/{environmentDefinitionId:guid}/containers/restart")]
    public async Task<IActionResult> RestartContainers(Guid id, Guid environmentDefinitionId, CancellationToken cancellationToken) =>
        Ok(await containerOperationsService.RestartAsync(id, environmentDefinitionId, cancellationToken));

    [HttpPost("{id:guid}/environments/{environmentDefinitionId:guid}/containers/start")]
    public async Task<IActionResult> StartContainers(Guid id, Guid environmentDefinitionId, CancellationToken cancellationToken) =>
        Ok(await containerOperationsService.StartAsync(id, environmentDefinitionId, cancellationToken));

    [HttpPost("{id:guid}/environments/{environmentDefinitionId:guid}/containers/stop")]
    public async Task<IActionResult> StopContainers(Guid id, Guid environmentDefinitionId, CancellationToken cancellationToken) =>
        Ok(await containerOperationsService.StopAsync(id, environmentDefinitionId, cancellationToken));

    /// <summary>Requests a build from this application's configured build server/job
    /// (never a caller-supplied job). Never deploys anything by itself — permission
    /// (builds.request) is enforced inside the service, same pattern as containers.</summary>
    [HttpPost("{id:guid}/builds")]
    public async Task<IActionResult> RequestBuild(Guid id, [FromBody] RequestBuildRequest request, CancellationToken cancellationToken) =>
        Ok(await buildService.RequestBuildAsync(id, request, cancellationToken));

    [HttpGet("{id:guid}/builds")]
    public async Task<IActionResult> GetBuilds(Guid id, CancellationToken cancellationToken) =>
        Ok(await buildService.ListAsync(id, cancellationToken));

    /// <summary>Immutable releases available to deploy for this (ContainerImage-mode)
    /// application — see DeployToDevAsync's ReleaseId parameter.</summary>
    [HttpGet("{id:guid}/releases")]
    public async Task<IActionResult> GetReleases(Guid id, CancellationToken cancellationToken) =>
        Ok(await buildService.ListReleasesAsync(id, cancellationToken));

    /// <summary>docker compose down -v / up -d — destroys volumes. Requires
    /// containers.recreate, an explicit Confirm:true body, and that this
    /// application environment has UseDownWithVolumesOnDeploy explicitly enabled
    /// (all enforced inside the service).</summary>
    [HttpPost("{id:guid}/environments/{environmentDefinitionId:guid}/containers/recreate")]
    public async Task<IActionResult> RecreateContainers(
        Guid id, Guid environmentDefinitionId, [FromBody] RecreateWithVolumesRequest request, CancellationToken cancellationToken) =>
        Ok(await containerOperationsService.RecreateWithVolumesAsync(id, environmentDefinitionId, request, cancellationToken));
}
