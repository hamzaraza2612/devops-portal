using DevOpsPortal.Application.Dtos.Applications;

namespace DevOpsPortal.Application.Services;

public interface IApplicationService
{
    Task<IReadOnlyList<ApplicationDto>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<ApplicationDto> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<ApplicationDto> CreateAsync(CreateApplicationRequest request, CancellationToken cancellationToken = default);
    Task<ApplicationDto> UpdateAsync(Guid id, UpdateApplicationRequest request, CancellationToken cancellationToken = default);

    /// <summary>Blocked while any Deployment exists for this application — use
    /// UpdateAsync with IsActive: false instead once it has ever been deployed.</summary>
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
