using DevOpsPortal.Application.Dtos.Users;
using DevOpsPortal.Domain.Entities;

namespace DevOpsPortal.Application.Common;

public static class UserMapper
{
    public static UserDto ToDto(User user, IReadOnlyList<string> roles, IReadOnlyList<string> permissions) => new(
        user.Id,
        user.Username,
        user.Email,
        user.FullName,
        user.IsActive,
        user.CreatedAt,
        user.LastLoginAt,
        roles,
        permissions);
}
