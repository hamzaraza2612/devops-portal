using DevOpsPortal.Application.Dtos.Roles;

namespace DevOpsPortal.Application.Services;

public interface IRoleService
{
    Task<IReadOnlyList<RoleDto>> GetAllRolesAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PermissionDto>> GetAllPermissionsAsync(CancellationToken cancellationToken = default);
}
