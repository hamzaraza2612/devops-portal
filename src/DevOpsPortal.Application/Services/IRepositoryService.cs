using DevOpsPortal.Application.Dtos.Repositories;

namespace DevOpsPortal.Application.Services;

public interface IRepositoryService
{
    Task<IReadOnlyList<RepositoryDto>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<RepositoryDto> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<RepositoryDto> CreateAsync(CreateRepositoryRequest request, CancellationToken cancellationToken = default);
    Task<RepositoryDto> UpdateAsync(Guid id, UpdateRepositoryRequest request, CancellationToken cancellationToken = default);

    Task<RepositoryDto> SetAccessTokenAsync(Guid id, SetRepositoryAccessTokenRequest request, CancellationToken cancellationToken = default);

    /// <summary>Master requirements §3 "Test GitLab Connection": actually reaches
    /// the configured GitLab instance and reports CONNECTED or FAILED with a
    /// useful error message — never fabricates a successful result.</summary>
    Task<RepositoryConnectionTestResultDto> TestConnectionAsync(Guid id, CancellationToken cancellationToken = default);
}
