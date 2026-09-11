using DevOpsPortal.Application.Dtos.Environments;

namespace DevOpsPortal.Application.Services;

/// <summary>The four pipeline stages (DEV/QA/UAT/PRODUCTION) are seeded
/// reference data whose Name and SortOrder are load-bearing throughout the
/// codebase — DeploymentService's promote/approve/deploy permission
/// dictionaries and the promotion pipeline's "previous environment" lookup
/// are all keyed by exactly these 4 names in this fixed order (see
/// PROJECT_STATE.md). Creating a genuinely new 5th environment or renaming/
/// reordering an existing one would silently produce an environment nobody
/// can ever be granted deploy/promote/approve permissions for — worse than
/// not offering the feature — so this interface deliberately does not
/// support either. UpdateAsync exposes only the two fields that are safe to
/// change without that risk: IsProductionLike (which environment requires
/// CTO approval) and IsActive (this entity's "remove" affordance — see its
/// own doc comment for why a hard delete isn't offered either).</summary>
public interface IEnvironmentDefinitionService
{
    Task<IReadOnlyList<EnvironmentDefinitionDto>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<EnvironmentDefinitionDto> UpdateAsync(
        Guid id, UpdateEnvironmentDefinitionRequest request, CancellationToken cancellationToken = default);
}
