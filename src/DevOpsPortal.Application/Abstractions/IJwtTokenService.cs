using DevOpsPortal.Domain.Entities;

namespace DevOpsPortal.Application.Abstractions;

public record GeneratedToken(string Token, DateTimeOffset ExpiresAt);

public interface IJwtTokenService
{
    GeneratedToken GenerateToken(User user, IEnumerable<string> roles, IEnumerable<string> permissions);
}
