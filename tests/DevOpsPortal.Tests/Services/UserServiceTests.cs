using DevOpsPortal.Application.Dtos.Users;
using DevOpsPortal.Application.Exceptions;
using DevOpsPortal.Application.Services;
using DevOpsPortal.Domain.Constants;
using DevOpsPortal.Infrastructure.Persistence;
using DevOpsPortal.Infrastructure.Security;
using DevOpsPortal.Tests.Common;
using Xunit;

namespace DevOpsPortal.Tests.Services;

public class UserServiceTests
{
    private static async Task<(UserService Service, AppDbContext Db, Guid DevEnvironmentId, Guid QaEnvironmentId)> CreateSutAsync()
    {
        var db = TestDb.CreateInMemory();
        await TestDb.SeedEnvironmentDefinitionsAsync(db);
        var hasher = new PasswordHasher();
        var audit = new AuditService(db, new FakeCurrentUserService());
        var devId = db.EnvironmentDefinitions.Single(e => e.Name == EnvironmentNames.Dev).Id;
        var qaId = db.EnvironmentDefinitions.Single(e => e.Name == EnvironmentNames.Qa).Id;
        return (new UserService(db, hasher, audit), db, devId, qaId);
    }

    [Fact]
    public async Task CreateAsync_WithValidRequest_CreatesUserWithEnvironmentAccess()
    {
        var (sut, db, devId, _) = await CreateSutAsync();

        var dto = await sut.CreateAsync(new CreateUserRequest("dave", "dave@example.local", "Dave Dev", "StrongPass1!", false, false, [devId]));

        Assert.Equal("dave", dto.Username);
        Assert.Contains(dto.EnvironmentAccess, e => e.Id == devId);
        Assert.Single(db.Users);
    }

    [Fact]
    public async Task CreateAsync_WithIsAdmin_CreatesAdminUser()
    {
        var (sut, _, _, _) = await CreateSutAsync();

        var dto = await sut.CreateAsync(new CreateUserRequest("admin1", "admin1@example.local", "Admin One", "StrongPass1!", true, false, []));

        Assert.True(dto.IsAdmin);
        Assert.Empty(dto.EnvironmentAccess);
    }

    [Fact]
    public async Task CreateAsync_WithDuplicateUsername_ThrowsConflict()
    {
        var (sut, _, devId, _) = await CreateSutAsync();
        await sut.CreateAsync(new CreateUserRequest("eve", "eve@example.local", "Eve", "StrongPass1!", false, false, [devId]));

        await Assert.ThrowsAsync<ConflictException>(() =>
            sut.CreateAsync(new CreateUserRequest("eve", "eve2@example.local", "Eve2", "StrongPass1!", false, false, [devId])));
    }

    [Fact]
    public async Task CreateAsync_WithWeakPassword_ThrowsValidation()
    {
        var (sut, _, devId, _) = await CreateSutAsync();

        await Assert.ThrowsAsync<ValidationException>(() =>
            sut.CreateAsync(new CreateUserRequest("frank", "frank@example.local", "Frank", "short", false, false, [devId])));
    }

    [Fact]
    public async Task CreateAsync_WithInvalidEnvironmentId_ThrowsValidation()
    {
        var (sut, _, _, _) = await CreateSutAsync();

        await Assert.ThrowsAsync<ValidationException>(() =>
            sut.CreateAsync(new CreateUserRequest("gina", "gina@example.local", "Gina", "StrongPass1!", false, false, [Guid.NewGuid()])));
    }

    [Fact]
    public async Task CreateAsync_WithNoEnvironmentsAndNotAdmin_CreatesUserWithNoAccess()
    {
        var (sut, _, _, _) = await CreateSutAsync();

        var dto = await sut.CreateAsync(new CreateUserRequest("noaccess", "noaccess@example.local", "No Access", "StrongPass1!", false, false, []));

        Assert.False(dto.IsAdmin);
        Assert.Empty(dto.EnvironmentAccess);
        Assert.Contains("No access", dto.Roles);
    }

    [Fact]
    public async Task UpdateAsync_ReplacesEnvironmentAccess()
    {
        var (sut, _, devId, qaId) = await CreateSutAsync();
        var created = await sut.CreateAsync(new CreateUserRequest("hank", "hank@example.local", "Hank", "StrongPass1!", false, false, [devId]));

        var updated = await sut.UpdateAsync(created.Id, new UpdateUserRequest("hank@example.local", "Hank Updated", true, false, false, [qaId]));

        Assert.DoesNotContain(updated.EnvironmentAccess, e => e.Id == devId);
        Assert.Contains(updated.EnvironmentAccess, e => e.Id == qaId);
    }

    [Fact]
    public async Task ChangeOwnPasswordAsync_WithWrongCurrentPassword_Throws()
    {
        var (sut, _, devId, _) = await CreateSutAsync();
        var created = await sut.CreateAsync(new CreateUserRequest("ivan", "ivan@example.local", "Ivan", "StrongPass1!", false, false, [devId]));

        await Assert.ThrowsAsync<ValidationException>(() =>
            sut.ChangeOwnPasswordAsync(created.Id, new ChangeOwnPasswordRequest("WrongPassword1!", "NewStrongPass1!")));
    }

    [Fact]
    public async Task ChangeOwnPasswordAsync_WithCorrectCurrentPassword_UpdatesHash()
    {
        var (sut, db, devId, _) = await CreateSutAsync();
        var created = await sut.CreateAsync(new CreateUserRequest("judy", "judy@example.local", "Judy", "StrongPass1!", false, false, [devId]));

        await sut.ChangeOwnPasswordAsync(created.Id, new ChangeOwnPasswordRequest("StrongPass1!", "NewStrongPass1!"));

        var user = await db.Users.FindAsync(created.Id);
        Assert.True(new PasswordHasher().Verify(user!.PasswordHash, "NewStrongPass1!"));
    }
}
