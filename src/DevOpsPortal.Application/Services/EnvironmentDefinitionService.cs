using DevOpsPortal.Application.Common;
using DevOpsPortal.Application.Dtos.Environments;
using DevOpsPortal.Application.Exceptions;
using DevOpsPortal.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace DevOpsPortal.Application.Services;

public class EnvironmentDefinitionService(IAppDbContext db, IAuditService auditService) : IEnvironmentDefinitionService
{
    public async Task<IReadOnlyList<EnvironmentDefinitionDto>> GetAllAsync(CancellationToken cancellationToken = default) =>
        await db.EnvironmentDefinitions
            .OrderBy(e => e.SortOrder)
            .Select(e => new EnvironmentDefinitionDto(e.Id, e.Name, e.SortOrder, e.IsProductionLike, e.IsActive))
            .ToListAsync(cancellationToken);

    public async Task<EnvironmentDefinitionDto> UpdateAsync(
        Guid id, UpdateEnvironmentDefinitionRequest request, CancellationToken cancellationToken = default)
    {
        var env = await db.EnvironmentDefinitions.FirstOrDefaultAsync(e => e.Id == id, cancellationToken)
            ?? throw new NotFoundException("EnvironmentDefinition", id);

        env.IsProductionLike = request.IsProductionLike;
        env.IsActive = request.IsActive;
        await db.SaveChangesAsync(cancellationToken);

        await auditService.LogAsync("environment.update", AuditResult.Success, "EnvironmentDefinition", env.Id.ToString(),
            details: $"Updated '{env.Name}'; productionLike={env.IsProductionLike}; active={env.IsActive}", cancellationToken: cancellationToken);

        return new EnvironmentDefinitionDto(env.Id, env.Name, env.SortOrder, env.IsProductionLike, env.IsActive);
    }
}
