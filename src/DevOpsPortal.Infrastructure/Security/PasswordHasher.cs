using DevOpsPortal.Application.Abstractions;
using DevOpsPortal.Domain.Entities;
using Microsoft.AspNetCore.Identity;

namespace DevOpsPortal.Infrastructure.Security;

/// <summary>PBKDF2-HMAC-SHA256 hashing via ASP.NET Core Identity's implementation.</summary>
public class PasswordHasher : IPasswordHasher
{
    private readonly PasswordHasher<User> _inner = new();

    public string Hash(string password) => _inner.HashPassword(null!, password);

    public bool Verify(string hash, string providedPassword)
    {
        var result = _inner.VerifyHashedPassword(null!, hash, providedPassword);
        return result is PasswordVerificationResult.Success or PasswordVerificationResult.SuccessRehashNeeded;
    }
}
