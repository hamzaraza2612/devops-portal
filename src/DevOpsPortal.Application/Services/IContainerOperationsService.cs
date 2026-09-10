using DevOpsPortal.Application.Dtos.Containers;

namespace DevOpsPortal.Application.Services;

/// <summary>
/// Docker container monitoring and operational controls (Phase 5) for a single
/// configured application environment. Every method resolves and enforces its
/// own permission internally (ContainersView/ContainersControl/ContainersRecreate
/// — see ContainerOperationsService), the same pattern IDeploymentService uses,
/// rather than a static [RequirePermission] attribute, so the check is exercised
/// directly by service-level tests without needing the HTTP pipeline.
/// </summary>
public interface IContainerOperationsService
{
    Task<ContainerEnvironmentStatusDto> GetStatusAsync(
        Guid applicationId, Guid environmentDefinitionId, CancellationToken cancellationToken = default);

    Task<ContainerActionResultDto> RestartAsync(
        Guid applicationId, Guid environmentDefinitionId, CancellationToken cancellationToken = default);

    Task<ContainerActionResultDto> StartAsync(
        Guid applicationId, Guid environmentDefinitionId, CancellationToken cancellationToken = default);

    Task<ContainerActionResultDto> StopAsync(
        Guid applicationId, Guid environmentDefinitionId, CancellationToken cancellationToken = default);

    /// <summary>`docker compose down -v` followed by `docker compose up -d` — destroys
    /// volumes. Only permitted when ApplicationEnvironment.UseDownWithVolumesOnDeploy
    /// has been explicitly turned on for this application+environment (the "explicitly
    /// configured" gate from the master requirements) AND the caller passes
    /// Confirm: true AND holds ContainersRecreate. Blocked while a deployment is
    /// already in progress for this environment.</summary>
    Task<ContainerActionResultDto> RecreateWithVolumesAsync(
        Guid applicationId, Guid environmentDefinitionId, RecreateWithVolumesRequest request, CancellationToken cancellationToken = default);
}
