namespace DevOpsPortal.Application.Dtos.Users;

public record UserDto(
    Guid Id,
    string Username,
    string Email,
    string FullName,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastLoginAt,
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> Permissions);

public record CreateUserRequest(
    string Username,
    string Email,
    string FullName,
    string Password,
    IReadOnlyList<Guid> RoleIds);

public record UpdateUserRequest(
    string Email,
    string FullName,
    bool IsActive,
    IReadOnlyList<Guid> RoleIds);

public record AdminResetPasswordRequest(string NewPassword);

public record ChangeOwnPasswordRequest(string CurrentPassword, string NewPassword);
