using DevOpsPortal.Application.Abstractions;
using DevOpsPortal.Application.Common;
using DevOpsPortal.Application.Dtos.TargetServers;
using DevOpsPortal.Application.Exceptions;
using DevOpsPortal.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace DevOpsPortal.Application.Services;

public class TargetServerService(IAppDbContext db, IAuditService auditService, ICurrentTenantService currentTenantService) : ITargetServerService
{
    public async Task<IReadOnlyList<TargetServerDto>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var servers = await db.TargetServers
            .Include(s => s.AllowedDeploymentRoots)
            .OrderBy(s => s.Name)
            .ToListAsync(cancellationToken);
        return servers.Select(ToDto).ToList();
    }

    public async Task<TargetServerDto> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var server = await db.TargetServers
            .Include(s => s.AllowedDeploymentRoots)
            .FirstOrDefaultAsync(s => s.Id == id, cancellationToken)
            ?? throw new NotFoundException("TargetServer", id);
        return ToDto(server);
    }

    public async Task<TargetServerDto> CreateAsync(CreateTargetServerRequest request, CancellationToken cancellationToken = default)
    {
        var name = request.Name.Trim();
        if (await db.TargetServers.AnyAsync(s => s.Name.ToLower() == name.ToLower(), cancellationToken))
            throw new ConflictException($"Target server name '{name}' is already in use.");

        var server = new TargetServer
        {
            TenantId = currentTenantService.RequireTenantId(),
            Name = name,
            Description = request.Description?.Trim(),
            Hostname = request.Hostname?.Trim(),
            IsActive = true,
        };
        db.TargetServers.Add(server);
        await db.SaveChangesAsync(cancellationToken);

        await auditService.LogAsync("targetserver.create", AuditResult.Success, "TargetServer", server.Id.ToString(),
            details: $"Created target server '{server.Name}'", cancellationToken: cancellationToken);

        return ToDto(server);
    }

    public async Task<TargetServerDto> UpdateAsync(Guid id, UpdateTargetServerRequest request, CancellationToken cancellationToken = default)
    {
        var server = await db.TargetServers
            .Include(s => s.AllowedDeploymentRoots)
            .FirstOrDefaultAsync(s => s.Id == id, cancellationToken)
            ?? throw new NotFoundException("TargetServer", id);

        var name = request.Name.Trim();
        if (await db.TargetServers.AnyAsync(s => s.Id != id && s.Name.ToLower() == name.ToLower(), cancellationToken))
            throw new ConflictException($"Target server name '{name}' is already in use.");

        server.Name = name;
        server.Description = request.Description?.Trim();
        server.Hostname = request.Hostname?.Trim();
        server.IsActive = request.IsActive;
        await db.SaveChangesAsync(cancellationToken);

        await auditService.LogAsync("targetserver.update", AuditResult.Success, "TargetServer", server.Id.ToString(),
            details: $"Updated target server '{server.Name}'; active={server.IsActive}", cancellationToken: cancellationToken);

        return ToDto(server);
    }

    public async Task<AllowedDeploymentRootDto> AddAllowedRootAsync(
        Guid targetServerId, CreateAllowedDeploymentRootRequest request, CancellationToken cancellationToken = default)
    {
        var server = await db.TargetServers.FirstOrDefaultAsync(s => s.Id == targetServerId, cancellationToken)
            ?? throw new NotFoundException("TargetServer", targetServerId);

        if (!DeploymentPathValidator.TryNormalize(request.RootPath, out var normalizedPath))
            throw new ValidationException("RootPath must be an absolute path with no '.' or '..' segments.");

        if (await db.AllowedDeploymentRoots.AnyAsync(
                r => r.TargetServerId == targetServerId && r.RootPath == normalizedPath, cancellationToken))
            throw new ConflictException($"'{normalizedPath}' is already an allowed root on this target server.");

        var root = new AllowedDeploymentRoot
        {
            TenantId = currentTenantService.RequireTenantId(),
            TargetServerId = targetServerId,
            RootPath = normalizedPath,
            Description = request.Description?.Trim(),
            IsActive = true,
        };
        db.AllowedDeploymentRoots.Add(root);
        await db.SaveChangesAsync(cancellationToken);

        await auditService.LogAsync("targetserver.allowedroot.add", AuditResult.Success, "TargetServer", targetServerId.ToString(),
            details: $"Added allowed deployment root '{normalizedPath}'", cancellationToken: cancellationToken);

        return ToRootDto(root);
    }

    public async Task<AllowedDeploymentRootDto> UpdateAllowedRootAsync(
        Guid targetServerId, Guid rootId, UpdateAllowedDeploymentRootRequest request, CancellationToken cancellationToken = default)
    {
        var root = await db.AllowedDeploymentRoots
            .FirstOrDefaultAsync(r => r.Id == rootId && r.TargetServerId == targetServerId, cancellationToken)
            ?? throw new NotFoundException("AllowedDeploymentRoot", rootId);

        if (!DeploymentPathValidator.TryNormalize(request.RootPath, out var normalizedPath))
            throw new ValidationException("RootPath must be an absolute path with no '.' or '..' segments.");

        if (await db.AllowedDeploymentRoots.AnyAsync(
                r => r.Id != rootId && r.TargetServerId == targetServerId && r.RootPath == normalizedPath, cancellationToken))
            throw new ConflictException($"'{normalizedPath}' is already an allowed root on this target server.");

        root.RootPath = normalizedPath;
        root.Description = request.Description?.Trim();
        root.IsActive = request.IsActive;
        await db.SaveChangesAsync(cancellationToken);

        await auditService.LogAsync("targetserver.allowedroot.update", AuditResult.Success, "TargetServer", targetServerId.ToString(),
            details: $"Updated allowed deployment root '{normalizedPath}'; active={root.IsActive}", cancellationToken: cancellationToken);

        return ToRootDto(root);
    }

    private static AllowedDeploymentRootDto ToRootDto(AllowedDeploymentRoot r) =>
        new(r.Id, r.TargetServerId, r.RootPath, r.Description, r.IsActive);

    private static TargetServerDto ToDto(TargetServer s) =>
        new(s.Id, s.Name, s.Description, s.Hostname, s.IsActive, s.CreatedAt,
            s.AllowedDeploymentRoots.OrderBy(r => r.RootPath).Select(ToRootDto).ToList());
}
