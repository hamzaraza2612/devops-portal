using DevOpsPortal.Application.Dtos.Environments;

namespace DevOpsPortal.Application.Dtos.Users;

public record UserDto(
    Guid Id,
    string Username,
    string Email,
    string FullName,
    bool IsActive,
    bool IsAdmin,
    bool CanApproveProduction,
    IReadOnlyList<EnvironmentDefinitionDto> EnvironmentAccess,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastLoginAt,
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> Permissions);

public record CreateUserRequest(
    string Username,
    string Email,
    string FullName,
    string Password,
    bool IsAdmin,
    bool CanApproveProduction,
    IReadOnlyList<Guid> EnvironmentDefinitionIds);

public record UpdateUserRequest(
    string Email,
    string FullName,
    bool IsActive,
    bool IsAdmin,
    bool CanApproveProduction,
    IReadOnlyList<Guid> EnvironmentDefinitionIds);

public record AdminResetPasswordRequest(string NewPassword);

public record ChangeOwnPasswordRequest(string CurrentPassword, string NewPassword);
