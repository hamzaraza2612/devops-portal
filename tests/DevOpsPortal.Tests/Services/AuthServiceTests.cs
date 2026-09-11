using DevOpsPortal.Application.Dtos.Auth;
using DevOpsPortal.Application.Exceptions;
using DevOpsPortal.Application.Services;
using DevOpsPortal.Domain.Entities;
using DevOpsPortal.Infrastructure.Security;
using DevOpsPortal.Tests.Common;
using Microsoft.Extensions.Options;
using Xunit;

namespace DevOpsPortal.Tests.Services;

public class AuthServiceTests
{
    private static (AuthService Service, Infrastructure.Persistence.AppDbContext Db) CreateSut()
    {
        var db = TestDb.CreateInMemory();
        var hasher = new PasswordHasher();
        var jwt = new JwtTokenService(Options.Create(new JwtSettings
        {
            Issuer = "test", Audience = "test", SigningKey = "unit-test-signing-key-at-least-32-bytes-long",
        }));
        var currentUser = new FakeCurrentUserService();
        var audit = new AuditService(db, currentUser, new FakeCurrentTenantService());
        return (new AuthService(db, hasher, jwt, audit), db);
    }

    private static async Task<User> SeedUserAsync(Infrastructure.Persistence.AppDbContext db, string username, string password, bool isActive = true)
    {
        var adminRole = await TestDb.SeedRolesAndPermissionsAsync(db);
        // TenantId intentionally left null (platform administrator) — this test is
        // about the login mechanics themselves, not tenant scoping; see
        // TenantIsolationTests for the multi-tenant login/isolation coverage.
        var user = new User
        {
            Username = username,
            Email = $"{username}@example.local",
            FullName = username,
            PasswordHash = new PasswordHasher().Hash(password),
            IsActive = isActive,
        };
        user.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = adminRole.Id });
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    [Fact]
    public async Task LoginAsync_WithCorrectCredentials_ReturnsTokenAndUpdatesLastLogin()
    {
        var (sut, db) = CreateSut();
        await SeedUserAsync(db, "alice", "CorrectHorse1!");

        var result = await sut.LoginAsync(new LoginRequest("alice", "CorrectHorse1!"));

        Assert.False(string.IsNullOrWhiteSpace(result.Token));
        Assert.Equal("alice", result.User.Username);
        Assert.Contains("ADMIN", result.User.Roles);
        Assert.NotNull((await db.Users.FindAsync(result.User.Id))!.LastLoginAt);
    }

    [Fact]
    public async Task LoginAsync_WithWrongPassword_ThrowsAndLogsFailure()
    {
        var (sut, db) = CreateSut();
        await SeedUserAsync(db, "bob", "CorrectHorse1!");

        await Assert.ThrowsAsync<AuthenticationFailedException>(() => sut.LoginAsync(new LoginRequest("bob", "wrong")));

        Assert.Contains(db.AuditLogs, a => a.Action == "auth.login" && a.Result == AuditResult.Failure);
    }

    [Fact]
    public async Task LoginAsync_WithUnknownUsername_ThrowsAuthenticationFailed()
    {
        var (sut, db) = CreateSut();
        await TestDb.SeedRolesAndPermissionsAsync(db);

        await Assert.ThrowsAsync<AuthenticationFailedException>(() => sut.LoginAsync(new LoginRequest("nobody", "whatever1")));
    }

    [Fact]
    public async Task LoginAsync_WithInactiveAccount_Throws()
    {
        var (sut, db) = CreateSut();
        await SeedUserAsync(db, "carol", "CorrectHorse1!", isActive: false);

        await Assert.ThrowsAsync<AuthenticationFailedException>(() => sut.LoginAsync(new LoginRequest("carol", "CorrectHorse1!")));
    }
}
