using DevOpsPortal.Application.Abstractions;
using DevOpsPortal.Application.Common;
using DevOpsPortal.Application.Dtos.Tenants;
using DevOpsPortal.Application.Exceptions;
using DevOpsPortal.Domain.Constants;
using DevOpsPortal.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace DevOpsPortal.Application.Services;

/// <summary>
/// Tenant CRUD, gated entirely by PermissionCodes.TenantsView/TenantsManage —
/// a platform-administrator-only concern (see RolesController and
/// TenantsController's [RequirePermission] attributes). CreateAsync also
/// provisions a new tenant's defaults in one step (the tenant's own ADMIN/
/// DEVOPS/DEVELOPER/QA/UAT/CTO role set, pipeline EnvironmentDefinitions, and
/// one initial tenant-admin user) — a tenant with none of that would have
/// nobody able to log in and configure it. This is a one-time provisioning
/// step, not idempotent seeding: a brand-new tenant has no pre-existing rows,
/// so unlike DataSeeder there is nothing to check for first.
/// </summary>
public class TenantService(IAppDbContext db, IPasswordHasher passwordHasher, IAuditService auditService) : ITenantService
{
    public async Task<IReadOnlyList<TenantDto>> GetAllAsync(CancellationToken cancellationToken = default) =>
        await db.Tenants.OrderBy(t => t.Name).Select(t => ToDto(t)).ToListAsync(cancellationToken);

    public async Task<TenantDto> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Id == id, cancellationToken)
            ?? throw new NotFoundException("Tenant", id);
        return ToDto(tenant);
    }

    public async Task<CreateTenantResponse> CreateAsync(CreateTenantRequest request, CancellationToken cancellationToken = default)
    {
        var name = request.Name.Trim();
        var slug = request.Slug.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(name))
            throw new ValidationException("Name is required.");
        if (!System.Text.RegularExpressions.Regex.IsMatch(slug, "^[a-z0-9]+(-[a-z0-9]+)*$"))
            throw new ValidationException("Slug must be lowercase alphanumeric segments separated by single hyphens (e.g. 'acme-corp').");
        if (await db.Tenants.AnyAsync(t => t.Slug == slug, cancellationToken))
            throw new ConflictException($"Slug '{slug}' is already in use.");

        var adminUsername = request.InitialAdminUsername.Trim();
        var adminEmail = request.InitialAdminEmail.Trim();
        if (string.IsNullOrWhiteSpace(adminUsername) || string.IsNullOrWhiteSpace(adminEmail))
            throw new ValidationException("InitialAdminUsername and InitialAdminEmail are required.");

        // Username/Email are globally unique across every tenant (see UserService) —
        // IgnoreQueryFilters so this check catches a collision with any tenant's user.
        if (await db.Users.IgnoreQueryFilters().AnyAsync(u => u.Username.ToLower() == adminUsername.ToLower(), cancellationToken))
            throw new ConflictException($"Username '{adminUsername}' is already in use.");
        if (await db.Users.IgnoreQueryFilters().AnyAsync(u => u.Email.ToLower() == adminEmail.ToLower(), cancellationToken))
            throw new ConflictException($"Email '{adminEmail}' is already in use.");

        var generatedPassword = string.IsNullOrWhiteSpace(request.InitialAdminPassword) ? RandomPasswordGenerator.Generate() : null;
        var adminPassword = generatedPassword ?? request.InitialAdminPassword!;

        var tenant = new Tenant { Name = name, Slug = slug, Description = request.Description?.Trim(), IsActive = true };
        db.Tenants.Add(tenant);

        var roles = new Dictionary<string, Role>();
        foreach (var roleName in RoleNames.All)
        {
            var role = new Role { TenantId = tenant.Id, Name = roleName, Description = $"{roleName} role", IsSystem = true };
            roles[roleName] = role;
            db.Roles.Add(role);
        }

        foreach (var (envName, sortOrder, isProductionLike) in EnvironmentNames.All)
        {
            db.EnvironmentDefinitions.Add(new EnvironmentDefinition
            {
                TenantId = tenant.Id,
                Name = envName,
                SortOrder = sortOrder,
                IsProductionLike = isProductionLike,
            });
        }

        var allPermissions = await db.Permissions.ToListAsync(cancellationToken);
        var permissionsByCode = allPermissions.ToDictionary(p => p.Code);

        // The tenant's own ADMIN role gets every current permission EXCEPT the
        // platform-administrator-only ones (TenantsView/TenantsManage) — those
        // govern tenants themselves and must never be reachable from within any
        // tenant's own role set, or a tenant admin could manage other tenants.
        var tenantAssignablePermissions = allPermissions.Where(p =>
            p.Code != PermissionCodes.TenantsView && p.Code != PermissionCodes.TenantsManage);
        foreach (var permission in tenantAssignablePermissions)
            db.RolePermissions.Add(new RolePermission { RoleId = roles[RoleNames.Admin].Id, PermissionId = permission.Id });

        foreach (var (roleName, codes) in DefaultRolePermissions.Defaults)
        {
            if (!roles.TryGetValue(roleName, out var role))
                continue;
            foreach (var code in codes)
            {
                if (permissionsByCode.TryGetValue(code, out var permission))
                    db.RolePermissions.Add(new RolePermission { RoleId = role.Id, PermissionId = permission.Id });
            }
        }

        var admin = new User
        {
            TenantId = tenant.Id,
            Username = adminUsername,
            Email = adminEmail,
            FullName = $"{name} Administrator",
            PasswordHash = passwordHasher.Hash(adminPassword),
            IsActive = true,
        };
        admin.UserRoles.Add(new UserRole { UserId = admin.Id, RoleId = roles[RoleNames.Admin].Id });
        db.Users.Add(admin);

        await db.SaveChangesAsync(cancellationToken);

        await auditService.LogAsync("tenant.create", AuditResult.Success, "Tenant", tenant.Id.ToString(),
            details: $"Created tenant '{tenant.Name}' (slug={tenant.Slug}) with initial admin '{admin.Username}'", cancellationToken: cancellationToken);

        return new CreateTenantResponse(ToDto(tenant), admin.Username, generatedPassword);
    }

    public async Task<TenantDto> UpdateAsync(Guid id, UpdateTenantRequest request, CancellationToken cancellationToken = default)
    {
        var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Id == id, cancellationToken)
            ?? throw new NotFoundException("Tenant", id);

        tenant.Name = request.Name.Trim();
        tenant.Description = request.Description?.Trim();
        tenant.IsActive = request.IsActive;
        tenant.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        await auditService.LogAsync("tenant.update", AuditResult.Success, "Tenant", tenant.Id.ToString(),
            details: $"Updated tenant '{tenant.Name}'; active={tenant.IsActive}", cancellationToken: cancellationToken);

        return ToDto(tenant);
    }

    private static TenantDto ToDto(Tenant t) => new(t.Id, t.Name, t.Slug, t.Description, t.IsActive, t.CreatedAt, t.UpdatedAt);
}
