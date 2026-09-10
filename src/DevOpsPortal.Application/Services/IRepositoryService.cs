using DevOpsPortal.Application.Dtos.Repositories;

namespace DevOpsPortal.Application.Services;

public interface IRepositoryService
{
    Task<IReadOnlyList<RepositoryDto>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<RepositoryDto> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<RepositoryDto> CreateAsync(CreateRepositoryRequest request, CancellationToken cancellationToken = default);
    Task<RepositoryDto> UpdateAsync(Guid id, UpdateRepositoryRequest request, CancellationToken cancellationToken = default);
}
