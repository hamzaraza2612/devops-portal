using DevOpsPortal.Application.Dtos.Builds;

namespace DevOpsPortal.Application.Services;

/// <summary>
/// Mode B (build-from-source) configuration — see master requirements §3.
/// Stores and validates configuration only; nothing here is executed. A
/// future Build Worker phase reads this to actually run a build.
/// </summary>
public interface IBuildConfigurationService
{
    Task<BuildConfigurationDto?> GetAsync(Guid applicationId, CancellationToken cancellationToken = default);

    Task<BuildConfigurationDto> UpsertAsync(
        Guid applicationId, UpsertBuildConfigurationRequest request, CancellationToken cancellationToken = default);
}
