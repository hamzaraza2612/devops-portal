using DevOpsPortal.Application.Dtos.Users;
using DevOpsPortal.Application.Exceptions;
using DevOpsPortal.Application.Services;
using DevOpsPortal.Domain.Entities;
using DevOpsPortal.Infrastructure.Persistence;
using DevOpsPortal.Infrastructure.Security;
using DevOpsPortal.Tests.Common;
using Xunit;

namespace DevOpsPortal.Tests.Services;

public class UserServiceTests
{
    private static async Task<(UserService Service, AppDbContext Db, Role AdminRole)> CreateSutAsync()
    {
        var db = TestDb.CreateInMemory();
        var adminRole = await TestDb.SeedRolesAndPermissionsAsync(db);
        var hasher = new PasswordHasher();
        var audit = new AuditService(db, new FakeCurrentUserService());
        return (new UserService(db, hasher, audit), db, adminRole);
    }

    [Fact]
    public async Task CreateAsync_WithValidRequest_CreatesUserWithRole()
    {
        var (sut, db, adminRole) = await CreateSutAsync();

        var dto = await sut.CreateAsync(new CreateUserRequest("dave", "dave@example.local", "Dave Dev", "StrongPass1!", [adminRole.Id]));

        Assert.Equal("dave", dto.Username);
        Assert.Contains("ADMIN", dto.Roles);
        Assert.Single(db.Users);
    }

    [Fact]
    public async Task CreateAsync_WithDuplicateUsername_ThrowsConflict()
    {
        var (sut, _, adminRole) = await CreateSutAsync();
        await sut.CreateAsync(new CreateUserRequest("eve", "eve@example.local", "Eve", "StrongPass1!", [adminRole.Id]));

        await Assert.ThrowsAsync<ConflictException>(() =>
            sut.CreateAsync(new CreateUserRequest("eve", "eve2@example.local", "Eve2", "StrongPass1!", [adminRole.Id])));
    }

    [Fact]
    public async Task CreateAsync_WithWeakPassword_ThrowsValidation()
    {
        var (sut, _, adminRole) = await CreateSutAsync();

        await Assert.ThrowsAsync<ValidationException>(() =>
            sut.CreateAsync(new CreateUserRequest("frank", "frank@example.local", "Frank", "short", [adminRole.Id])));
    }

    [Fact]
    public async Task CreateAsync_WithNoRoles_ThrowsValidation()
    {
        var (sut, _, _) = await CreateSutAsync();

        await Assert.ThrowsAsync<ValidationException>(() =>
            sut.CreateAsync(new CreateUserRequest("gina", "gina@example.local", "Gina", "StrongPass1!", [])));
    }

    [Fact]
    public async Task UpdateAsync_ReplacesRoleAssignments()
    {
        var (sut, db, adminRole) = await CreateSutAsync();
        var created = await sut.CreateAsync(new CreateUserRequest("hank", "hank@example.local", "Hank", "StrongPass1!", [adminRole.Id]));
        var otherRole = db.Roles.Single(r => r.Name == Domain.Constants.RoleNames.Developer);

        var updated = await sut.UpdateAsync(created.Id, new UpdateUserRequest("hank@example.local", "Hank Updated", true, [otherRole.Id]));

        Assert.DoesNotContain("ADMIN", updated.Roles);
        Assert.Contains("DEVELOPER", updated.Roles);
    }

    [Fact]
    public async Task ChangeOwnPasswordAsync_WithWrongCurrentPassword_Throws()
    {
        var (sut, _, adminRole) = await CreateSutAsync();
        var created = await sut.CreateAsync(new CreateUserRequest("ivan", "ivan@example.local", "Ivan", "StrongPass1!", [adminRole.Id]));

        await Assert.ThrowsAsync<ValidationException>(() =>
            sut.ChangeOwnPasswordAsync(created.Id, new ChangeOwnPasswordRequest("WrongPassword1!", "NewStrongPass1!")));
    }

    [Fact]
    public async Task ChangeOwnPasswordAsync_WithCorrectCurrentPassword_UpdatesHash()
    {
        var (sut, db, adminRole) = await CreateSutAsync();
        var created = await sut.CreateAsync(new CreateUserRequest("judy", "judy@example.local", "Judy", "StrongPass1!", [adminRole.Id]));

        await sut.ChangeOwnPasswordAsync(created.Id, new ChangeOwnPasswordRequest("StrongPass1!", "NewStrongPass1!"));

        var user = await db.Users.FindAsync(created.Id);
        Assert.True(new PasswordHasher().Verify(user!.PasswordHash, "NewStrongPass1!"));
    }
}
