using DevOpsPortal.Application.Dtos.Users;

namespace DevOpsPortal.Application.Services;

public interface IUserService
{
    Task<IReadOnlyList<UserDto>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<UserDto> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<UserDto> CreateAsync(CreateUserRequest request, CancellationToken cancellationToken = default);
    Task<UserDto> UpdateAsync(Guid id, UpdateUserRequest request, CancellationToken cancellationToken = default);
    Task ResetPasswordAsync(Guid id, AdminResetPasswordRequest request, CancellationToken cancellationToken = default);
    Task ChangeOwnPasswordAsync(Guid userId, ChangeOwnPasswordRequest request, CancellationToken cancellationToken = default);
}
