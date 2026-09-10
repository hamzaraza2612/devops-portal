using DevOpsPortal.Application.Dtos.TargetServers;

namespace DevOpsPortal.Application.Services;

public interface ITargetServerService
{
    Task<IReadOnlyList<TargetServerDto>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<TargetServerDto> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<TargetServerDto> CreateAsync(CreateTargetServerRequest request, CancellationToken cancellationToken = default);
    Task<TargetServerDto> UpdateAsync(Guid id, UpdateTargetServerRequest request, CancellationToken cancellationToken = default);

    Task<AllowedDeploymentRootDto> AddAllowedRootAsync(
        Guid targetServerId, CreateAllowedDeploymentRootRequest request, CancellationToken cancellationToken = default);

    Task<AllowedDeploymentRootDto> UpdateAllowedRootAsync(
        Guid targetServerId, Guid rootId, UpdateAllowedDeploymentRootRequest request, CancellationToken cancellationToken = default);
}
