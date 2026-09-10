using DevOpsPortal.Application.Common;
using DevOpsPortal.Application.Dtos.Environments;
using Microsoft.EntityFrameworkCore;

namespace DevOpsPortal.Application.Services;

public class EnvironmentDefinitionService(IAppDbContext db) : IEnvironmentDefinitionService
{
    public async Task<IReadOnlyList<EnvironmentDefinitionDto>> GetAllAsync(CancellationToken cancellationToken = default) =>
        await db.EnvironmentDefinitions
            .OrderBy(e => e.SortOrder)
            .Select(e => new EnvironmentDefinitionDto(e.Id, e.Name, e.SortOrder, e.IsProductionLike, e.IsActive))
            .ToListAsync(cancellationToken);
}
