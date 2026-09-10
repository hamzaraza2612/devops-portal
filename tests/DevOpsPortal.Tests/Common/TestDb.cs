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
}

public class FakeCurrentUserService : ICurrentUserService
{
    public Guid? UserId { get; set; }
    public string? Username { get; set; }
    public string? IpAddress { get; set; } = "127.0.0.1";
}
