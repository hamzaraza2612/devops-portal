using System.Security.Cryptography;
using DevOpsPortal.Application.Abstractions;
using DevOpsPortal.Domain.Constants;
using DevOpsPortal.Domain.Entities;
using DevOpsPortal.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace DevOpsPortal.Infrastructure.Seed;

/// <summary>
/// Idempotent startup seeding: system roles, the permission catalog, role→permission
/// grants, and a bootstrap admin account when no users exist yet.
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

        foreach (var name in RoleNames.All)
        {
            if (!await db.Roles.AnyAsync(r => r.Name == name))
                db.Roles.Add(new Role { Name = name, Description = $"{name} role", IsSystem = true });
        }
        await db.SaveChangesAsync();

        foreach (var (name, sortOrder, isProductionLike) in EnvironmentNames.All)
        {
            if (!await db.EnvironmentDefinitions.AnyAsync(e => e.Name == name))
            {
                db.EnvironmentDefinitions.Add(new EnvironmentDefinition
                {
                    Name = name,
                    SortOrder = sortOrder,
                    IsProductionLike = isProductionLike,
                });
            }
        }
        await db.SaveChangesAsync();

        var adminRole = await db.Roles.FirstAsync(r => r.Name == RoleNames.Admin);
        var allPermissions = await db.Permissions.ToListAsync();
        foreach (var permission in allPermissions)
        {
            var hasGrant = await db.RolePermissions.AnyAsync(rp => rp.RoleId == adminRole.Id && rp.PermissionId == permission.Id);
            if (!hasGrant)
                db.RolePermissions.Add(new RolePermission { RoleId = adminRole.Id, PermissionId = permission.Id });
        }
        await db.SaveChangesAsync();

        await GrantDefaultRolePermissionsAsync(db);

        if (!await db.Users.AnyAsync())
        {
            var username = config["Seed:AdminUsername"] ?? "admin";
            var email = config["Seed:AdminEmail"] ?? "admin@example.local";
            var password = config["Seed:AdminPassword"];

            var generated = false;
            if (string.IsNullOrWhiteSpace(password))
            {
                password = GenerateRandomPassword();
                generated = true;
            }

            var admin = new User
            {
                Username = username,
                Email = email,
                FullName = "Portal Administrator",
                PasswordHash = hasher.Hash(password),
                IsActive = true,
            };
            admin.UserRoles.Add(new UserRole { UserId = admin.Id, RoleId = adminRole.Id });

            db.Users.Add(admin);
            await db.SaveChangesAsync();

            if (generated)
            {
                logger.LogWarning(
                    "No admin password configured (Seed:AdminPassword / ADMIN_INITIAL_PASSWORD). " +
                    "Generated bootstrap credentials — username '{Username}', password '{Password}'. " +
                    "Log in and change this password immediately.", username, password);
            }
            else
            {
                logger.LogInformation("Bootstrap admin user '{Username}' created.", username);
            }
        }
    }

    /// <summary>
    /// Default, idempotent role→permission grants for the non-admin roles, per
    /// master requirements §9. Additive only — never removes a grant, so any
    /// future manual customization (once role-permission mutation ships) is
    /// preserved; re-running this just fills in anything still missing.
    /// </summary>
    private static async Task GrantDefaultRolePermissionsAsync(AppDbContext db)
    {
        var defaults = new Dictionary<string, string[]>
        {
            [RoleNames.Developer] =
            [
                PermissionCodes.ApplicationsView,
                PermissionCodes.EnvironmentsView,
                PermissionCodes.DeploymentsView,
                PermissionCodes.DeploymentsDeployDev,
                PermissionCodes.DeploymentsPromoteQa,
                PermissionCodes.ContainersView,
            ],
            [RoleNames.Qa] =
            [
                PermissionCodes.ApplicationsView,
                PermissionCodes.EnvironmentsView,
                PermissionCodes.DeploymentsView,
                PermissionCodes.DeploymentsApproveQa,
                PermissionCodes.DeploymentsDeployQa,
                PermissionCodes.ContainersView,
            ],
            [RoleNames.Uat] =
            [
                PermissionCodes.ApplicationsView,
                PermissionCodes.EnvironmentsView,
                PermissionCodes.DeploymentsView,
                PermissionCodes.DeploymentsApproveUat,
                PermissionCodes.DeploymentsDeployUat,
                PermissionCodes.ContainersView,
            ],
            [RoleNames.DevOps] =
            [
                PermissionCodes.ApplicationsView,
                PermissionCodes.RepositoriesView,
                PermissionCodes.TargetServersView,
                PermissionCodes.EnvironmentsView,
                PermissionCodes.DeploymentsView,
                PermissionCodes.DeploymentsDeployDev,
                PermissionCodes.DeploymentsPromoteQa,
                PermissionCodes.DeploymentsApproveQa,
                PermissionCodes.DeploymentsDeployQa,
                PermissionCodes.DeploymentsPromoteUat,
                PermissionCodes.DeploymentsApproveUat,
                PermissionCodes.DeploymentsDeployUat,
                PermissionCodes.DeploymentsPromoteProduction,
                PermissionCodes.DeploymentsDeployProduction,
                PermissionCodes.DeploymentsRollback,
                PermissionCodes.ContainersView,
                PermissionCodes.ContainersControl,
                PermissionCodes.ContainersRecreate,
            ],
            [RoleNames.Cto] =
            [
                PermissionCodes.ApplicationsView,
                PermissionCodes.EnvironmentsView,
                PermissionCodes.DeploymentsView,
                PermissionCodes.DeploymentsApproveProduction,
                PermissionCodes.ContainersView,
            ],
        };

        foreach (var (roleName, codes) in defaults)
        {
            var role = await db.Roles.FirstOrDefaultAsync(r => r.Name == roleName);
            if (role is null)
                continue;

            foreach (var code in codes)
            {
                var permission = await db.Permissions.FirstOrDefaultAsync(p => p.Code == code);
                if (permission is null)
                    continue;

                var hasGrant = await db.RolePermissions.AnyAsync(rp => rp.RoleId == role.Id && rp.PermissionId == permission.Id);
                if (!hasGrant)
                    db.RolePermissions.Add(new RolePermission { RoleId = role.Id, PermissionId = permission.Id });
            }
        }

        await db.SaveChangesAsync();
    }

    private static string GenerateRandomPassword()
    {
        const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789!@#$%";
        var bytes = RandomNumberGenerator.GetBytes(20);
        return new string(bytes.Select(b => chars[b % chars.Length]).ToArray());
    }
}
