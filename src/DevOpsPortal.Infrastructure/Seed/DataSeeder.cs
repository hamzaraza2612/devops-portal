using DevOpsPortal.Application.Abstractions;
using DevOpsPortal.Application.Common;
using DevOpsPortal.Domain.Constants;
using DevOpsPortal.Domain.Entities;
using DevOpsPortal.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace DevOpsPortal.Infrastructure.Seed;

/// <summary>
/// Idempotent startup seeding of GLOBAL, platform-level data only: the
/// permission catalog, the single system-wide PLATFORM_ADMIN role, and a
/// bootstrap platform-administrator account when none exists yet. Runs with
/// no tenant ambient (the app has just started — there is no HTTP request),
/// so every query here is naturally scoped to TenantId == null by AppDbContext's
/// global query filters, exactly matching "global/platform-level only".
///
/// Per-tenant defaults (the ADMIN/DEVOPS/DEVELOPER/QA/UAT/CTO role set, pipeline
/// EnvironmentDefinitions, and their default permission grants) are NOT seeded
/// here — they're provisioned once per tenant, at tenant-creation time, by
/// TenantService. See DefaultRolePermissions for the shared default grants.
/// </summary>
public static class DataSeeder
{
    public static async Task SeedAsync(AppDbContext db, IPasswordHasher hasher, IConfiguration config, ILogger logger)
    {
        await db.Database.MigrateAsync();

        foreach (var (code, description) in PermissionCodes.All)
        {
            if (!await db.Permissions.AnyAsync(p => p.Code == code))
                db.Permissions.Add(new Permission { Code = code, Description = description });
        }
        await db.SaveChangesAsync();

        if (!await db.Roles.AnyAsync(r => r.Name == RoleNames.PlatformAdmin))
            db.Roles.Add(new Role { Name = RoleNames.PlatformAdmin, Description = "Platform administrator", IsSystem = true });
        await db.SaveChangesAsync();

        var platformAdminRole = await db.Roles.FirstAsync(r => r.Name == RoleNames.PlatformAdmin);
        var allPermissions = await db.Permissions.ToListAsync();
        foreach (var permission in allPermissions)
        {
            var hasGrant = await db.RolePermissions.AnyAsync(rp => rp.RoleId == platformAdminRole.Id && rp.PermissionId == permission.Id);
            if (!hasGrant)
                db.RolePermissions.Add(new RolePermission { RoleId = platformAdminRole.Id, PermissionId = permission.Id });
        }
        await db.SaveChangesAsync();

        if (!await db.Users.AnyAsync())
        {
            var username = config["Seed:AdminUsername"] ?? "admin";
            var email = config["Seed:AdminEmail"] ?? "admin@example.local";
            var password = config["Seed:AdminPassword"];

            var generated = false;
            if (string.IsNullOrWhiteSpace(password))
            {
                password = RandomPasswordGenerator.Generate();
                generated = true;
            }

            var admin = new User
            {
                TenantId = null,
                Username = username,
                Email = email,
                FullName = "Platform Administrator",
                PasswordHash = hasher.Hash(password),
                IsActive = true,
            };
            admin.UserRoles.Add(new UserRole { UserId = admin.Id, RoleId = platformAdminRole.Id });

            db.Users.Add(admin);
            await db.SaveChangesAsync();

            if (generated)
            {
                logger.LogWarning(
                    "No admin password configured (Seed:AdminPassword / ADMIN_INITIAL_PASSWORD). " +
                    "Generated bootstrap platform-administrator credentials — username '{Username}', password '{Password}'. " +
                    "Log in and change this password immediately.", username, password);
            }
            else
            {
                logger.LogInformation("Bootstrap platform-administrator user '{Username}' created.", username);
            }
        }
    }
}
