using DevOpsPortal.Application.Common;
using DevOpsPortal.Application.Dtos.Environments;
using DevOpsPortal.Application.Exceptions;
using DevOpsPortal.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace DevOpsPortal.Application.Services;

public class EnvironmentDefinitionService(IAppDbContext db, IAuditService auditService) : IEnvironmentDefinitionService
{
    public async Task<IReadOnlyList<EnvironmentDefinitionDto>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var environments = await db.EnvironmentDefinitions
            .Include(e => e.PrimaryTargetServer)
            .OrderBy(e => e.SortOrder)
            .ToListAsync(cancellationToken);
        return environments.Select(ToDto).ToList();
    }

    public async Task<EnvironmentDefinitionDto> UpdateAsync(
        Guid id, UpdateEnvironmentDefinitionRequest request, CancellationToken cancellationToken = default)
    {
        var env = await db.EnvironmentDefinitions
            .Include(e => e.PrimaryTargetServer)
            .FirstOrDefaultAsync(e => e.Id == id, cancellationToken)
            ?? throw new NotFoundException("EnvironmentDefinition", id);

        if (request.PrimaryTargetServerId is { } serverId &&
            !await db.TargetServers.AnyAsync(s => s.Id == serverId, cancellationToken))
        {
            throw new ValidationException("PrimaryTargetServerId does not refer to a known target server.");
        }

        env.IsProductionLike = request.IsProductionLike;
        env.IsActive = request.IsActive;
        env.PrimaryTargetServerId = request.PrimaryTargetServerId;
        await db.SaveChangesAsync(cancellationToken);

        // Reload the navigation property fresh so the returned DTO reflects the
        // server name for the (possibly just-changed) PrimaryTargetServerId
        // rather than a stale Include from before SaveChangesAsync.
        var refreshed = await db.EnvironmentDefinitions
            .Include(e => e.PrimaryTargetServer)
            .FirstAsync(e => e.Id == id, cancellationToken);

        await auditService.LogAsync("environment.update", AuditResult.Success, "EnvironmentDefinition", env.Id.ToString(),
            details: $"Updated '{env.Name}'; productionLike={env.IsProductionLike}; active={env.IsActive}; " +
                     $"primaryTargetServer={refreshed.PrimaryTargetServer?.Name ?? "(none)"}",
            cancellationToken: cancellationToken);

        return ToDto(refreshed);
    }

    private static EnvironmentDefinitionDto ToDto(EnvironmentDefinition e) =>
        new(e.Id, e.Name, e.SortOrder, e.IsProductionLike, e.IsActive, e.PrimaryTargetServerId, e.PrimaryTargetServer?.Name);
}
