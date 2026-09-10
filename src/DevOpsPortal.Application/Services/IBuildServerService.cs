using DevOpsPortal.Application.Dtos.Builds;

namespace DevOpsPortal.Application.Services;

public interface IBuildServerService
{
    Task<IReadOnlyList<BuildServerDto>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<BuildServerDto> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<BuildServerDto> CreateAsync(CreateBuildServerRequest request, CancellationToken cancellationToken = default);
    Task<BuildServerDto> UpdateAsync(Guid id, UpdateBuildServerRequest request, CancellationToken cancellationToken = default);
}
