using DevOpsPortal.Application.Abstractions;
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

    /// <summary>Removes the config row itself — blocked while any Deployment
    /// references it (use IsActive: false via UpsertAsync instead once
    /// deployment history exists).</summary>
    Task DeleteAsync(Guid applicationId, Guid environmentDefinitionId, CancellationToken cancellationToken = default);

    /// <summary>Read-only GitLab lookup using this app's Repository and this environment's
    /// configured BranchName. Never auto-deploys — purely informational. Fails gracefully
    /// (Success=false) rather than throwing if the repository/branch isn't reachable.</summary>
    Task<GitProviderResult<GitCommitInfo>> GetLatestCommitAsync(
        Guid applicationId, Guid environmentDefinitionId, CancellationToken cancellationToken = default);

    Task<GitProviderResult<IReadOnlyList<GitCommitInfo>>> GetRecentCommitsAsync(
        Guid applicationId, Guid environmentDefinitionId, int count, CancellationToken cancellationToken = default);
}
