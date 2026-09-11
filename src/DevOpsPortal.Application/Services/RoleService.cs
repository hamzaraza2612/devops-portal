using DevOpsPortal.Application.Abstractions;
using DevOpsPortal.Application.Common;
using DevOpsPortal.Application.Dtos.Roles;
using DevOpsPortal.Application.Exceptions;
using DevOpsPortal.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace DevOpsPortal.Application.Services;

public class RoleService(IAppDbContext db, IAuditService auditService, ICurrentTenantService currentTenantService) : IRoleService
{
    public async Task<IReadOnlyList<RoleDto>> GetAllRolesAsync(CancellationToken cancellationToken = default)
    {
        return await db.Roles
            .OrderBy(r => r.Name)
            .Select(r => new RoleDto(
                r.Id,
                r.Name,
                r.Description,
                r.IsSystem,
                r.RolePermissions.Select(rp => rp.Permission.Code).OrderBy(c => c).ToList()))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<PermissionDto>> GetAllPermissionsAsync(CancellationToken cancellationToken = default)
    {
        return await db.Permissions
            .OrderBy(p => p.Code)
            .Select(p => new PermissionDto(p.Id, p.Code, p.Description))
            .ToListAsync(cancellationToken);
    }

    /// <summary>Creates an organization-specific role in the caller's own tenant
    /// (master requirements Phase 9 §3: "support organization-specific
    /// roles/permissions"). Always IsSystem = false — system roles are seeded
    /// once, globally or per-tenant at provisioning, never through this API.</summary>
    public async Task<RoleDto> CreateAsync(CreateRoleRequest request, CancellationToken cancellationToken = default)
    {
        var tenantId = currentTenantService.RequireTenantId();
        var name = request.Name.Trim();
        if (string.IsNullOrWhiteSpace(name))
            throw new ValidationException("Name is required.");

        if (await db.Roles.AnyAsync(r => r.Name.ToLower() == name.ToLower(), cancellationToken))
            throw new ConflictException($"Role name '{name}' is already in use.");

        var permissions = await ResolvePermissionsAsync(request.PermissionIds, cancellationToken);

        var role = new Role
        {
            TenantId = tenantId,
            Name = name,
            Description = request.Description.Trim(),
            IsSystem = false,
        };
        foreach (var permission in permissions)
            role.RolePermissions.Add(new RolePermission { RoleId = role.Id, PermissionId = permission.Id });

        db.Roles.Add(role);
        await db.SaveChangesAsync(cancellationToken);

        await auditService.LogAsync("role.create", AuditResult.Success, "Role", role.Id.ToString(),
            details: $"Created role '{role.Name}' with [{string.Join(", ", permissions.Select(p => p.Code))}]", cancellationToken: cancellationToken);

        return new RoleDto(role.Id, role.Name, role.Description, role.IsSystem, permissions.Select(p => p.Code).OrderBy(c => c).ToList());
    }

    public async Task<RoleDto> UpdateAsync(Guid id, UpdateRoleRequest request, CancellationToken cancellationToken = default)
    {
        var role = await db.Roles.Include(r => r.RolePermissions)
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken)
            ?? throw new NotFoundException("Role", id);

        if (role.IsSystem)
            throw new ForbiddenException("System roles cannot be modified.");

        var permissions = await ResolvePermissionsAsync(request.PermissionIds, cancellationToken);

        role.Description = request.Description.Trim();
        foreach (var link in role.RolePermissions.ToList())
            db.RolePermissions.Remove(link);
        foreach (var permission in permissions)
            db.RolePermissions.Add(new RolePermission { RoleId = role.Id, PermissionId = permission.Id });

        await db.SaveChangesAsync(cancellationToken);

        await auditService.LogAsync("role.update", AuditResult.Success, "Role", role.Id.ToString(),
            details: $"Updated role '{role.Name}' permissions to [{string.Join(", ", permissions.Select(p => p.Code))}]", cancellationToken: cancellationToken);

        return new RoleDto(role.Id, role.Name, role.Description, role.IsSystem, permissions.Select(p => p.Code).OrderBy(c => c).ToList());
    }

    private async Task<List<Permission>> ResolvePermissionsAsync(IReadOnlyList<Guid> permissionIds, CancellationToken cancellationToken)
    {
        var distinctIds = permissionIds.Distinct().ToList();
        var permissions = await db.Permissions.Where(p => distinctIds.Contains(p.Id)).ToListAsync(cancellationToken);
        if (permissions.Count != distinctIds.Count)
            throw new ValidationException("One or more permission IDs are invalid.");
        return permissions;
    }
}
