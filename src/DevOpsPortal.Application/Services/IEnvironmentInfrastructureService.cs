using DevOpsPortal.Application.Abstractions;
using DevOpsPortal.Application.Dtos.Containers;
using DevOpsPortal.Application.Dtos.Infrastructure;

namespace DevOpsPortal.Application.Services;

/// <summary>
/// The Environment Infrastructure Dashboard's real-server-discovery
/// counterpart to IContainerOperationsService (which stays exactly as it was,
/// scoped to one Application × Environment pairing with a configured compose
/// path). This service is scoped to one EnvironmentDefinition alone, resolves
/// its PrimaryTargetServer, and shows EVERY container Docker reports on that
/// host — mapped to a configured Application when one is recognizable,
/// otherwise shown clearly as unregistered. Never requires a single
/// ApplicationEnvironment row to exist before it can show real containers.
/// </summary>
public interface IEnvironmentInfrastructureService
{
    Task<EnvironmentInfrastructureDto> GetInfrastructureAsync(Guid environmentDefinitionId, CancellationToken cancellationToken = default);

    /// <summary>Start/Stop/Restart a container discovered on this environment's
    /// target server, by container id — re-validated against a fresh discovery
    /// pass before anything is run (see RemoteContainerAction's doc comment for
    /// why "recreate with volumes" isn't offered here).</summary>
    Task<ContainerActionResultDto> RunContainerActionAsync(
        Guid environmentDefinitionId, string containerId, RemoteContainerAction action, CancellationToken cancellationToken = default);

    Task<DiscoveredContainerLogsDto> GetContainerLogsAsync(
        Guid environmentDefinitionId, string containerId, int tailLines, CancellationToken cancellationToken = default);
}
