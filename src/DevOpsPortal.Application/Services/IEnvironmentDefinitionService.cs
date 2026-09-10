using DevOpsPortal.Application.Dtos.Environments;

namespace DevOpsPortal.Application.Services;

/// <summary>Read-only in Phase 2 — the four pipeline stages are seeded reference
/// data, same pattern as Roles in Phase 1. Mutation can be added later if a
/// tenant needs custom stages.</summary>
public interface IEnvironmentDefinitionService
{
    Task<IReadOnlyList<EnvironmentDefinitionDto>> GetAllAsync(CancellationToken cancellationToken = default);
}
