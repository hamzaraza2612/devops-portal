using DevOpsPortal.Application.Dtos.Applications;

namespace DevOpsPortal.Application.Services;

public interface IApplicationEnvironmentService
{
    Task<IReadOnlyList<ApplicationEnvironmentDto>> GetForApplicationAsync(Guid applicationId, CancellationToken cancellationToken = default);

    Task<ApplicationEnvironmentDto> GetAsync(
        Guid applicationId, Guid environmentDefinitionId, CancellationToken cancellationToken = default);

    /// <summary>Creates or replaces the single config row for (applicationId, environmentDefinitionId).</summary>
    Task<ApplicationEnvironmentDto> UpsertAsync(
        Guid applicationId, Guid environmentDefinitionId, UpsertApplicationEnvironmentRequest request, CancellationToken cancellationToken = default);
}
