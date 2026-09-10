using DevOpsPortal.Application.Common;
using DevOpsPortal.Application.Dtos.Roles;
using Microsoft.EntityFrameworkCore;

namespace DevOpsPortal.Application.Services;

public class RoleService(IAppDbContext db) : IRoleService
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
}
