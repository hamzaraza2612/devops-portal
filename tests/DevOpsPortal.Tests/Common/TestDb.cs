using DevOpsPortal.Application.Abstractions;
using DevOpsPortal.Domain.Constants;
using DevOpsPortal.Domain.Entities;
using DevOpsPortal.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DevOpsPortal.Tests.Common;

public static class TestDb
{
    public static AppDbContext CreateInMemory()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }

    /// <summary>Seeds the fixed role/permission catalog directly — mirrors DataSeeder
    /// without depending on Database.MigrateAsync (unsupported by the InMemory provider).</summary>
    public static async Task<Role> SeedRolesAndPermissionsAsync(AppDbContext db)
    {
        foreach (var (code, description) in PermissionCodes.All)
            db.Permissions.Add(new Permission { Code = code, Description = description });

        var roles = RoleNames.All.Select(name => new Role { Name = name, Description = name, IsSystem = true }).ToList();
        db.Roles.AddRange(roles);
        await db.SaveChangesAsync();

        var adminRole = roles.Single(r => r.Name == RoleNames.Admin);
        foreach (var permission in db.Permissions)
            db.RolePermissions.Add(new RolePermission { RoleId = adminRole.Id, PermissionId = permission.Id });
        await db.SaveChangesAsync();

        return adminRole;
    }

    public static async Task SeedEnvironmentDefinitionsAsync(AppDbContext db)
    {
        foreach (var (name, sortOrder, isProductionLike) in EnvironmentNames.All)
            db.EnvironmentDefinitions.Add(new EnvironmentDefinition { Name = name, SortOrder = sortOrder, IsProductionLike = isProductionLike });
        await db.SaveChangesAsync();
    }

    /// <summary>Creates a user with exactly the given permissions (via a dedicated
    /// single-purpose role) — lets RBAC tests state precisely what a caller can and
    /// cannot do without depending on DataSeeder's broader default role grants.
    /// Requires Permissions already seeded (via SeedRolesAndPermissionsAsync).</summary>
    public static async Task<Guid> CreateUserWithPermissionsAsync(AppDbContext db, string username, params string[] permissionCodes)
    {
        var role = new Role { Name = $"test-role-{Guid.NewGuid():N}", Description = "test", IsSystem = false };
        db.Roles.Add(role);

        foreach (var code in permissionCodes)
        {
            var permission = await db.Permissions.FirstOrDefaultAsync(p => p.Code == code)
                ?? throw new InvalidOperationException($"Permission '{code}' is not seeded in this test db.");
            db.RolePermissions.Add(new RolePermission { RoleId = role.Id, PermissionId = permission.Id });
        }

        var user = new User { Username = username, Email = $"{username}@example.local", FullName = username, PasswordHash = "x", IsActive = true };
        db.Users.Add(user);
        db.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = role.Id });
        await db.SaveChangesAsync();

        return user.Id;
    }
}

public class FakeCurrentUserService : ICurrentUserService
{
    public Guid? UserId { get; set; }
    public string? Username { get; set; }
    public string? IpAddress { get; set; } = "127.0.0.1";
}

/// <summary>Always reports the provider as unreachable — exercises the "graceful
/// failure" path without any real network access.</summary>
public class FakeGitProviderClient : IGitProviderClient
{
    public Task<GitProviderResult<GitCommitInfo>> GetLatestCommitAsync(
        Repository repository, string branch, CancellationToken cancellationToken = default) =>
        Task.FromResult(GitProviderResult<GitCommitInfo>.Fail("Fake provider: not reachable in tests."));

    public Task<GitProviderResult<IReadOnlyList<GitCommitInfo>>> GetRecentCommitsAsync(
        Repository repository, string branch, int count, CancellationToken cancellationToken = default) =>
        Task.FromResult(GitProviderResult<IReadOnlyList<GitCommitInfo>>.Fail("Fake provider: not reachable in tests."));
}
