namespace DevOpsPortal.Application.Dtos.Roles;

public record RoleDto(Guid Id, string Name, string Description, bool IsSystem, IReadOnlyList<string> Permissions);

public record PermissionDto(Guid Id, string Code, string Description);

/// <summary>Creates an organization-specific role, scoped to the caller's own
/// tenant — never a system role (see Role.IsSystem).</summary>
public record CreateRoleRequest(string Name, string Description, IReadOnlyList<Guid> PermissionIds);

/// <summary>Rejected for a system role (Role.IsSystem) — those are shared
/// platform defaults, not any one tenant's to edit.</summary>
public record UpdateRoleRequest(string Description, IReadOnlyList<Guid> PermissionIds);
