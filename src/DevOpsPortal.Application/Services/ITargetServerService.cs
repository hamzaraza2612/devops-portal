using DevOpsPortal.Application.Dtos.TargetServers;

namespace DevOpsPortal.Application.Services;

public interface ITargetServerService
{
    Task<IReadOnlyList<TargetServerDto>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<TargetServerDto> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<TargetServerDto> CreateAsync(CreateTargetServerRequest request, CancellationToken cancellationToken = default);
    Task<TargetServerDto> UpdateAsync(Guid id, UpdateTargetServerRequest request, CancellationToken cancellationToken = default);

    /// <summary>Blocked while any ApplicationEnvironment references this server —
    /// use UpdateAsync with IsActive: false instead.</summary>
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    Task<AllowedDeploymentRootDto> AddAllowedRootAsync(
        Guid targetServerId, CreateAllowedDeploymentRootRequest request, CancellationToken cancellationToken = default);

    Task<AllowedDeploymentRootDto> UpdateAllowedRootAsync(
        Guid targetServerId, Guid rootId, UpdateAllowedDeploymentRootRequest request, CancellationToken cancellationToken = default);

    Task<TargetServerDto> SetSshCredentialAsync(Guid id, SetSshCredentialRequest request, CancellationToken cancellationToken = default);

    Task<TargetServerDto> SetSshPassphraseAsync(Guid id, SetSshPassphraseRequest request, CancellationToken cancellationToken = default);

    /// <summary>Master requirements §6 "Test Connection": actually connects to the
    /// target server over SSH and verifies connectivity, the authenticated user, and
    /// Docker/Compose availability — never fabricates a successful result.</summary>
    Task<TargetServerConnectionTestResultDto> TestConnectionAsync(Guid id, CancellationToken cancellationToken = default);
}
