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
/// Idempotent startup seeding for the single-organization model introduced in
/// Phase 12 (see PROJECT_STATE.md — multi-tenancy and the Role/Permission
/// catalog were removed): the four pipeline EnvironmentDefinitions
/// (DEV/QA/UAT/PRODUCTION) and a bootstrap administrator account when no user
/// exists yet. There is no per-tenant provisioning step anymore — this is the
/// only seeding the application performs.
/// </summary>
public static class DataSeeder
{
    public static async Task SeedAsync(AppDbContext db, IPasswordHasher hasher, IConfiguration config, ILogger logger)
    {
        await db.Database.MigrateAsync();

        foreach (var (name, sortOrder, isProductionLike) in EnvironmentNames.All)
        {
            if (!await db.EnvironmentDefinitions.AnyAsync(e => e.Name == name))
            {
                db.EnvironmentDefinitions.Add(new EnvironmentDefinition
                {
                    Name = name,
                    SortOrder = sortOrder,
                    IsProductionLike = isProductionLike,
                    IsActive = true,
                });
            }
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
                Username = username,
                Email = email,
                FullName = "Administrator",
                PasswordHash = hasher.Hash(password),
                IsActive = true,
                IsAdmin = true,
                CanApproveProduction = true,
            };

            db.Users.Add(admin);
            await db.SaveChangesAsync();

            if (generated)
            {
                logger.LogWarning(
                    "No admin password configured (Seed:AdminPassword / ADMIN_INITIAL_PASSWORD). " +
                    "Generated bootstrap administrator credentials — username '{Username}', password '{Password}'. " +
                    "Log in and change this password immediately.", username, password);
            }
            else
            {
                logger.LogInformation("Bootstrap administrator user '{Username}' created.", username);
            }
        }
    }
}
