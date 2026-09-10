using DevOpsPortal.Application.Dtos.Auth;

namespace DevOpsPortal.Application.Services;

public interface IAuthService
{
    Task<LoginResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default);
}
