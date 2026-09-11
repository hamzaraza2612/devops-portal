using DevOpsPortal.Application.Dtos.Builds;

namespace DevOpsPortal.Application.Services;

public interface IBuildService
{
    Task<BuildRequestDto> RequestBuildAsync(Guid applicationId, RequestBuildRequest request, CancellationToken cancellationToken = default);

    /// <summary>Refreshes and returns the build's current status (refresh-on-read —
    /// no background poller; see PROJECT_STATE.md's Phase 6 architecture notes).</summary>
    Task<BuildRequestDto> GetAsync(Guid buildRequestId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<BuildRequestDto>> ListAsync(Guid? applicationId, CancellationToken cancellationToken = default);

    Task<BuildLogDto> GetLogAsync(Guid buildRequestId, CancellationToken cancellationToken = default);

    /// <summary>Immutable releases available to deploy for a ContainerImage-mode
    /// application (master requirements §16/§17) — every successful build's Release
    /// row, newest first.</summary>
    Task<IReadOnlyList<ReleaseDto>> ListReleasesAsync(Guid applicationId, CancellationToken cancellationToken = default);
}
