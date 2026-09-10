namespace DevOpsPortal.Application.Dtos.Roles;

public record RoleDto(Guid Id, string Name, string Description, bool IsSystem, IReadOnlyList<string> Permissions);

public record PermissionDto(Guid Id, string Code, string Description);
