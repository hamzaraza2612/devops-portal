using System.IdentityModel.Tokens.Jwt;
using DevOpsPortal.Domain.Entities;
using DevOpsPortal.Infrastructure.Security;
using Microsoft.Extensions.Options;
using Xunit;

namespace DevOpsPortal.Tests.Security;

public class JwtTokenServiceTests
{
    private static JwtTokenService CreateService() => new(Options.Create(new JwtSettings
    {
        Issuer = "test-issuer",
        Audience = "test-audience",
        SigningKey = "unit-test-signing-key-at-least-32-bytes-long",
        ExpiryMinutes = 60,
    }));

    [Fact]
    public void GenerateToken_IncludesRoleAndPermissionClaims()
    {
        var service = CreateService();
        var user = new User { Username = "alice", Email = "alice@example.local" };

        var result = service.GenerateToken(user, ["ADMIN"], ["users.manage", "roles.view"]);

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(result.Token);
        Assert.Contains(jwt.Claims, c => c.Type == "permission" && c.Value == "users.manage");
        Assert.Contains(jwt.Claims, c => c.Type == "permission" && c.Value == "roles.view");
        Assert.Contains(jwt.Claims, c => c.Value == "ADMIN");
        Assert.Equal("test-issuer", jwt.Issuer);
    }

    [Fact]
    public void GenerateToken_SetsExpiryAccordingToSettings()
    {
        var service = CreateService();
        var user = new User { Username = "alice", Email = "alice@example.local" };

        var before = DateTimeOffset.UtcNow;
        var result = service.GenerateToken(user, [], []);

        Assert.True(result.ExpiresAt > before.AddMinutes(59));
        Assert.True(result.ExpiresAt < before.AddMinutes(61));
    }
}
