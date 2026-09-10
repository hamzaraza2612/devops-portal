using DevOpsPortal.Application.Dtos.Secrets;
using DevOpsPortal.Domain.Enums;

namespace DevOpsPortal.Application.Services;

public interface ISecretReferenceService
{
    Task<IReadOnlyList<SecretReferenceDto>> ListAsync(
        Guid? applicationId, Guid? environmentDefinitionId, SecretCategory? category, CancellationToken cancellationToken = default);

    Task<SecretReferenceDto> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task<SecretReferenceDto> CreateAsync(CreateSecretReferenceRequest request, CancellationToken cancellationToken = default);

    Task<SecretReferenceDto> UpdateAsync(Guid id, UpdateSecretReferenceRequest request, CancellationToken cancellationToken = default);

    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Internal — for IDeploymentExecutor only, never called from a
    /// controller. Resolves every secret visible to (applicationId,
    /// environmentDefinitionId) — ApplicationEnvironment-scoped secrets for
    /// this exact pair, Application-scoped secrets for this application, and
    /// Global secrets — keyed by name, most-specific scope winning on a name
    /// collision. actorUserId/actorUsername attribute the resulting
    /// "secret.referenced" audit entries to the human who requested the
    /// deployment, since this runs in the background worker with no
    /// HTTP-request-scoped current user. Throws DeploymentExecutionException
    /// (never returns a partial/corrupt result) if any resolution fails.</summary>
    Task<IReadOnlyDictionary<string, string>> ResolveForDeploymentAsync(
        Guid applicationId, Guid environmentDefinitionId, Guid actorUserId, string? actorUsername, CancellationToken cancellationToken = default);
}
