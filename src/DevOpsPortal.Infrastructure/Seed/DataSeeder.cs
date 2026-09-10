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

        var adminRole = await db.Roles.FirstAsync(r => r.Name == RoleNames.Admin);
        var allPermissions = await db.Permissions.ToListAsync();
        foreach (var permission in allPermissions)
        {
            var hasGrant = await db.RolePermissions.AnyAsync(rp => rp.RoleId == adminRole.Id && rp.PermissionId == permission.Id);
            if (!hasGrant)
                db.RolePermissions.Add(new RolePermission { RoleId = adminRole.Id, PermissionId = permission.Id });
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

    private static string GenerateRandomPassword()
    {
        const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789!@#$%";
        var bytes = RandomNumberGenerator.GetBytes(20);
        return new string(bytes.Select(b => chars[b % chars.Length]).ToArray());
    }
}
