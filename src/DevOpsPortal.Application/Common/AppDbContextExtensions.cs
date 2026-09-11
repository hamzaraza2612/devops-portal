using Microsoft.EntityFrameworkCore;

namespace DevOpsPortal.Application.Common;

public static class AppDbContextExtensions
{
    public static async Task<(IReadOnlyList<string> Roles, IReadOnlyList<string> Permissions)> GetRolesAndPermissionsAsync(
        this IAppDbContext db, Guid userId, CancellationToken cancellationToken = default)
    {
        // IgnoreQueryFilters: this resolves a specific, already-known userId's own
        // roles/permissions (e.g. during login, before any tenant context exists).
        // Safe because UserRoles rows are only ever created linking a user to a role
        // within that user's own tenant — there is no cross-tenant data to leak here.
        var roles = await db.UserRoles
            .IgnoreQueryFilters()
            .Where(ur => ur.UserId == userId)
            .Select(ur => ur.Role.Name)
            .ToListAsync(cancellationToken);

        var permissions = await db.UserRoles
            .IgnoreQueryFilters()
            .Where(ur => ur.UserId == userId)
            .SelectMany(ur => ur.Role.RolePermissions.Select(rp => rp.Permission.Code))
            .Distinct()
            .ToListAsync(cancellationToken);

        return (roles, permissions);
    }
}
