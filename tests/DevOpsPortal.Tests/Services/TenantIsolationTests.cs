using DevOpsPortal.Application.Common;
using DevOpsPortal.Application.Dtos.Applications;
using DevOpsPortal.Application.Dtos.Roles;
using DevOpsPortal.Application.Dtos.Tenants;
using DevOpsPortal.Application.Dtos.Users;
using DevOpsPortal.Application.Exceptions;
using DevOpsPortal.Application.Services;
using DevOpsPortal.Domain.Constants;
using DevOpsPortal.Domain.Entities;
using DevOpsPortal.Domain.Enums;
using DevOpsPortal.Infrastructure.Persistence;
using DevOpsPortal.Tests.Common;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DevOpsPortal.Tests.Services;

/// <summary>
/// Phase 9 §7: aggressively tests that a user/service scoped to one tenant can
/// never see or act on another tenant's applications, deployments, logs,
/// repositories, users, or roles — the core promise of AppDbContext's global
/// query filters. Every test here shares one physical InMemory database across
/// two (or more) tenants and flips the ambient FakeCurrentTenantService between
/// them, which is exactly the scenario the query filters exist to guard: many
/// tenants' rows living side by side in the same tables.
/// </summary>
public class TenantIsolationTests
{
    private sealed record Env(AppDbContext Db, FakeCurrentTenantService TenantContext, Guid TenantA, Guid TenantB);

    private static Env CreateEnv()
    {
        var db = TestDb.CreateInMemory(out var tenantContext);
        return new Env(db, tenantContext, Guid.NewGuid(), Guid.NewGuid());
    }

    private static ApplicationService CreateApplicationService(Env env)
    {
        var audit = new AuditService(env.Db, new FakeCurrentUserService(), env.TenantContext);
        return new ApplicationService(env.Db, audit, env.TenantContext);
    }

    [Fact]
    public async Task ApplicationService_GetAllAsync_NeverReturnsAnotherTenantsApplications()
    {
        var env = CreateEnv();
        var apps = CreateApplicationService(env);

        env.TenantContext.TenantId = env.TenantA;
        var appA = await apps.CreateAsync(new CreateApplicationRequest("App A", "app-a", null, DeploymentMode.LegacyFilesystem, null, null));

        env.TenantContext.TenantId = env.TenantB;
        var appB = await apps.CreateAsync(new CreateApplicationRequest("App B", "app-b", null, DeploymentMode.LegacyFilesystem, null, null));

        env.TenantContext.TenantId = env.TenantA;
        var visibleToA = await apps.GetAllAsync();
        Assert.Single(visibleToA);
        Assert.Equal(appA.Id, visibleToA[0].Id);

        env.TenantContext.TenantId = env.TenantB;
        var visibleToB = await apps.GetAllAsync();
        Assert.Single(visibleToB);
        Assert.Equal(appB.Id, visibleToB[0].Id);
    }

    [Fact]
    public async Task ApplicationService_GetByIdAsync_CrossTenantId_ThrowsNotFound_NeverLeaksTheOtherTenantsRecord()
    {
        var env = CreateEnv();
        var apps = CreateApplicationService(env);

        env.TenantContext.TenantId = env.TenantA;
        var appA = await apps.CreateAsync(new CreateApplicationRequest("App A", "app-a", null, DeploymentMode.LegacyFilesystem, null, null));

        env.TenantContext.TenantId = env.TenantB;
        await Assert.ThrowsAsync<NotFoundException>(() => apps.GetByIdAsync(appA.Id));
    }

    [Fact]
    public async Task ApplicationService_TwoTenants_CanUseTheSameSlug_WithoutConflict()
    {
        // Slug uniqueness is tenant-scoped (see AppDbContext's composite index) —
        // two different organizations both naming an app "sample-app" is normal,
        // not a collision.
        var env = CreateEnv();
        var apps = CreateApplicationService(env);

        env.TenantContext.TenantId = env.TenantA;
        await apps.CreateAsync(new CreateApplicationRequest("Sample", "sample-app", null, DeploymentMode.LegacyFilesystem, null, null));

        env.TenantContext.TenantId = env.TenantB;
        var exception = await Record.ExceptionAsync(() =>
            apps.CreateAsync(new CreateApplicationRequest("Sample", "sample-app", null, DeploymentMode.LegacyFilesystem, null, null)));

        Assert.Null(exception);
    }

    [Fact]
    public async Task DeploymentsAndLogs_AreInvisibleAcrossTenants_EvenViaDirectQuery()
    {
        var env = CreateEnv();

        env.TenantContext.TenantId = env.TenantA;
        var appA = new ManagedApplication { TenantId = env.TenantA, Name = "App A", Slug = "app-a", DeploymentMode = DeploymentMode.LegacyFilesystem };
        env.Db.Applications.Add(appA);
        await env.Db.SaveChangesAsync();
        var envDefA = new EnvironmentDefinition { TenantId = env.TenantA, Name = EnvironmentNames.Dev, SortOrder = 0 };
        env.Db.EnvironmentDefinitions.Add(envDefA);
        await env.Db.SaveChangesAsync();

        var deploymentA = new Deployment
        {
            TenantId = env.TenantA,
            ApplicationId = appA.Id,
            EnvironmentDefinitionId = envDefA.Id,
            ApplicationEnvironmentId = Guid.NewGuid(),
            CommitSha = "abc1234",
            RequestedByUserId = Guid.NewGuid(),
        };
        env.Db.Deployments.Add(deploymentA);
        await env.Db.SaveChangesAsync();
        env.Db.DeploymentLogEntries.Add(new DeploymentLogEntry
        {
            TenantId = env.TenantA, DeploymentId = deploymentA.Id, Sequence = 1, Message = "tenant A's secret deploy log line",
        });
        await env.Db.SaveChangesAsync();

        env.TenantContext.TenantId = env.TenantB;

        Assert.Empty(await env.Db.Applications.ToListAsync());
        Assert.Empty(await env.Db.Deployments.ToListAsync());
        Assert.Empty(await env.Db.DeploymentLogEntries.ToListAsync());
        // Direct-by-id lookups must also come back empty — not just list endpoints.
        Assert.Null(await env.Db.Deployments.FirstOrDefaultAsync(d => d.Id == deploymentA.Id));
        Assert.Null(await env.Db.DeploymentLogEntries.FirstOrDefaultAsync(l => l.DeploymentId == deploymentA.Id));

        env.TenantContext.TenantId = env.TenantA;
        Assert.Single(await env.Db.Deployments.ToListAsync());
        Assert.Single(await env.Db.DeploymentLogEntries.ToListAsync());
    }

    [Fact]
    public async Task Repositories_AreInvisibleAcrossTenants()
    {
        var env = CreateEnv();

        env.TenantContext.TenantId = env.TenantA;
        env.Db.Repositories.Add(new Repository { TenantId = env.TenantA, Name = "repo-a", Url = "https://git.example.com/a.git" });
        await env.Db.SaveChangesAsync();

        env.TenantContext.TenantId = env.TenantB;
        Assert.Empty(await env.Db.Repositories.ToListAsync());

        // And a same-named repository in tenant B is not a conflict (tenant-scoped uniqueness).
        env.Db.Repositories.Add(new Repository { TenantId = env.TenantB, Name = "repo-a", Url = "https://git.example.com/b.git" });
        var exception = await Record.ExceptionAsync(() => env.Db.SaveChangesAsync());
        Assert.Null(exception);
    }

    [Fact]
    public async Task Users_AreInvisibleAcrossTenants_ViaUserServiceListing()
    {
        var env = CreateEnv();

        env.TenantContext.TenantId = env.TenantA;
        await TestDb.SeedRolesAndPermissionsAsync(env.Db, env.TenantA);
        var hasher = new DevOpsPortal.Infrastructure.Security.PasswordHasher();
        var currentTenant = env.TenantContext;
        var audit = new AuditService(env.Db, new FakeCurrentUserService(), currentTenant);
        var userService = new UserService(env.Db, hasher, audit, currentTenant);

        var adminRoleA = await env.Db.Roles.SingleAsync(r => r.Name == RoleNames.Admin);
        var createdA = await userService.CreateAsync(new CreateUserRequest("alice", "alice@a.example", "Alice A", "Password123!", [adminRoleA.Id]));

        env.TenantContext.TenantId = env.TenantB;
        await TestDb.SeedRolesAndPermissionsAsync(env.Db, env.TenantB);

        // Tenant B's admin listing must not include tenant A's "alice".
        var usersVisibleToB = await userService.GetAllAsync();
        Assert.DoesNotContain(usersVisibleToB, u => u.Id == createdA.Id);

        // But GetByIdAsync for tenant A's user, called while scoped to tenant B, must 404 — not leak the record.
        await Assert.ThrowsAsync<NotFoundException>(() => userService.GetByIdAsync(createdA.Id));

        // Usernames stay globally unique across tenants (login needs this) even though
        // everything else is tenant-scoped — tenant B cannot register "alice" again.
        var adminRoleB = await env.Db.Roles.SingleAsync(r => r.Name == RoleNames.Admin);
        await Assert.ThrowsAsync<ConflictException>(() =>
            userService.CreateAsync(new CreateUserRequest("alice", "alice2@b.example", "Alice B", "Password123!", [adminRoleB.Id])));
    }

    [Fact]
    public async Task Roles_TwoTenants_CanEachHaveTheirOwnAdminRole_WithoutCollision()
    {
        var env = CreateEnv();

        env.TenantContext.TenantId = env.TenantA;
        var adminA = await TestDb.SeedRolesAndPermissionsAsync(env.Db, env.TenantA);

        env.TenantContext.TenantId = env.TenantB;
        var adminB = await TestDb.SeedRolesAndPermissionsAsync(env.Db, env.TenantB);

        Assert.NotEqual(adminA.Id, adminB.Id);
        Assert.Equal(RoleNames.Admin, adminA.Name);
        Assert.Equal(RoleNames.Admin, adminB.Name);

        // Tenant B's role listing sees only its own "ADMIN", not tenant A's.
        var rolesVisibleToB = await env.Db.Roles.ToListAsync();
        Assert.Single(rolesVisibleToB, r => r.Name == RoleNames.Admin);
        Assert.Equal(adminB.Id, rolesVisibleToB.Single(r => r.Name == RoleNames.Admin).Id);
    }

    [Fact]
    public async Task RolePermissionCheck_UserFromTenantA_CannotUseAPermissionOnlyGrantedInTenantB()
    {
        // RBAC must combine correctly with tenant isolation: holding a permission
        // in tenant B's role graph must never satisfy a check for a tenant-A user.
        var env = CreateEnv();

        env.TenantContext.TenantId = env.TenantA;
        await TestDb.SeedRolesAndPermissionsAsync(env.Db, env.TenantA);
        var restrictedUserId = await TestDb.CreateUserWithPermissionsAsync(env.Db, "restricted", env.TenantA, PermissionCodes.ApplicationsView);

        env.TenantContext.TenantId = env.TenantB;
        await TestDb.SeedRolesAndPermissionsAsync(env.Db, env.TenantB);
        var powerfulUserId = await TestDb.CreateUserWithPermissionsAsync(env.Db, "powerful", env.TenantB, PermissionCodes.ApplicationsManage);

        // GetRolesAndPermissionsAsync deliberately ignores the tenant filter (it
        // resolves a specific, already-known user's own roles — see its doc
        // comment) so this must reflect each user's OWN grants only, never the
        // other tenant's.
        var (_, restrictedPermissions) = await env.Db.GetRolesAndPermissionsAsync(restrictedUserId);
        Assert.DoesNotContain(PermissionCodes.ApplicationsManage, restrictedPermissions);

        var (_, powerfulPermissions) = await env.Db.GetRolesAndPermissionsAsync(powerfulUserId);
        Assert.Contains(PermissionCodes.ApplicationsManage, powerfulPermissions);
        Assert.DoesNotContain(PermissionCodes.ApplicationsManage, restrictedPermissions);
    }

    [Fact]
    public async Task TenantService_CreateAsync_NeverGrantsTheNewTenantsAdminRole_PlatformOnlyTenantPermissions()
    {
        // The single most important admin-permission boundary in Phase 9: a
        // tenant's own ADMIN role — however powerful within that tenant — must
        // never be able to manage OTHER tenants.
        var db = TestDb.CreateInMemory(out var tenantContext);
        tenantContext.TenantId = null; // platform administrator context
        await SeedGlobalPermissionCatalogAsync(db);

        var audit = new AuditService(db, new FakeCurrentUserService(), tenantContext);
        var passwordHasher = new DevOpsPortal.Infrastructure.Security.PasswordHasher();
        var tenantService = new TenantService(db, passwordHasher, audit);

        var response = await tenantService.CreateAsync(new CreateTenantRequest(
            "Acme Corp", "acme-corp", null, "acme-admin", "admin@acme.example", "Password123!"));

        var tenantAdminRole = await db.Roles.IgnoreQueryFilters()
            .Include(r => r.RolePermissions).ThenInclude(rp => rp.Permission)
            .SingleAsync(r => r.TenantId == response.Tenant.Id && r.Name == RoleNames.Admin);

        var grantedCodes = tenantAdminRole.RolePermissions.Select(rp => rp.Permission.Code).ToList();
        Assert.DoesNotContain(PermissionCodes.TenantsView, grantedCodes);
        Assert.DoesNotContain(PermissionCodes.TenantsManage, grantedCodes);
    }

    [Fact]
    public async Task TenantService_CreateAsync_ProvisionsWorkingDefaults_AndTheInitialAdminCanImmediatelyLogIn()
    {
        var db = TestDb.CreateInMemory(out var tenantContext);
        tenantContext.TenantId = null;
        await SeedGlobalPermissionCatalogAsync(db);

        var audit = new AuditService(db, new FakeCurrentUserService(), tenantContext);
        var passwordHasher = new DevOpsPortal.Infrastructure.Security.PasswordHasher();
        var tenantService = new TenantService(db, passwordHasher, audit);

        var response = await tenantService.CreateAsync(new CreateTenantRequest(
            "Acme Corp", "acme-corp", "A test tenant", "acme-admin", "admin@acme.example", null));

        Assert.NotNull(response.GeneratedPassword);
        Assert.Equal("acme-admin", response.InitialAdminUsername);

        tenantContext.TenantId = response.Tenant.Id;
        Assert.Equal(RoleNames.All.Count, await db.Roles.CountAsync());
        Assert.Equal(EnvironmentNames.All.Count, await db.EnvironmentDefinitions.CountAsync());

        var jwt = new DevOpsPortal.Infrastructure.Security.JwtTokenService(Microsoft.Extensions.Options.Options.Create(
            new DevOpsPortal.Infrastructure.Security.JwtSettings { Issuer = "test", Audience = "test", SigningKey = "unit-test-signing-key-at-least-32-bytes-long" }));
        var authService = new AuthService(db, passwordHasher, jwt, audit);
        tenantContext.TenantId = null; // login itself happens pre-tenant-context, like a real request
        var login = await authService.LoginAsync(new DevOpsPortal.Application.Dtos.Auth.LoginRequest("acme-admin", response.GeneratedPassword!));

        Assert.Contains(RoleNames.Admin, login.User.Roles);
        Assert.Contains(PermissionCodes.ApplicationsManage, login.User.Permissions);
        Assert.DoesNotContain(PermissionCodes.TenantsManage, login.User.Permissions);
    }

    [Fact]
    public async Task TenantService_UpdateAsync_DeactivatingATenant_LocksOutItsUsersAtLogin()
    {
        var db = TestDb.CreateInMemory(out var tenantContext);
        tenantContext.TenantId = null;
        await SeedGlobalPermissionCatalogAsync(db);

        var audit = new AuditService(db, new FakeCurrentUserService(), tenantContext);
        var passwordHasher = new DevOpsPortal.Infrastructure.Security.PasswordHasher();
        var tenantService = new TenantService(db, passwordHasher, audit);

        var response = await tenantService.CreateAsync(new CreateTenantRequest(
            "Acme Corp", "acme-corp", null, "acme-admin", "admin@acme.example", "Password123!"));

        await tenantService.UpdateAsync(response.Tenant.Id, new UpdateTenantRequest("Acme Corp", null, IsActive: false));

        var jwt = new DevOpsPortal.Infrastructure.Security.JwtTokenService(Microsoft.Extensions.Options.Options.Create(
            new DevOpsPortal.Infrastructure.Security.JwtSettings { Issuer = "test", Audience = "test", SigningKey = "unit-test-signing-key-at-least-32-bytes-long" }));
        var authService = new AuthService(db, passwordHasher, jwt, audit);

        await Assert.ThrowsAsync<AuthenticationFailedException>(() =>
            authService.LoginAsync(new DevOpsPortal.Application.Dtos.Auth.LoginRequest("acme-admin", "Password123!")));
    }

    private static async Task SeedGlobalPermissionCatalogAsync(AppDbContext db)
    {
        foreach (var (code, description) in PermissionCodes.All)
            db.Permissions.Add(new Permission { Code = code, Description = description });
        await db.SaveChangesAsync();
    }
}
