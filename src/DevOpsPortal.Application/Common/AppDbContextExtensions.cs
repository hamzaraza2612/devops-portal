using DevOpsPortal.Domain.Constants;
using Microsoft.EntityFrameworkCore;

namespace DevOpsPortal.Application.Common;

/// <summary>
/// Synthesizes a user's effective permission set from the simplified Phase 12
/// authorization model (see PROJECT_STATE.md — replaced the earlier Role/
/// Permission catalog): <c>User.IsAdmin</c>, <c>User.CanApproveProduction</c>,
/// and their granted <see cref="UserEnvironmentAccess"/> rows. Deliberately the
/// ONLY thing that changed — every caller (AuthService for JWT claims,
/// EnsurePermissionAsync in every service, [RequirePermission] on every
/// controller) still checks the exact same PermissionCodes strings it always
/// did, so none of that enforcement code needed to change at all.
/// </summary>
public static class AppDbContextExtensions
{
    /// <summary>Permissions granted to anyone holding access to at least one
    /// environment — these gate actions that are further narrowed per-request by
    /// which specific application/environment/server is involved, not by the
    /// permission claim alone (e.g. ContainersControl is granted here, but
    /// ContainerOperationsService still checks the specific environment's access
    /// before acting — see EnsureEnvironmentAccessAsync).</summary>
    private static readonly string[] BaseGrantedWithAnyEnvironmentAccess =
    [
        PermissionCodes.ApplicationsView,
        PermissionCodes.EnvironmentsView,
        PermissionCodes.DeploymentsView,
        PermissionCodes.ContainersView,
        PermissionCodes.ContainersControl,
        PermissionCodes.ContainersRecreate,
        PermissionCodes.BuildsView,
        PermissionCodes.BuildsRequest,
        PermissionCodes.SecretsView,
        PermissionCodes.SecretsReveal,
    ];

    private static readonly Dictionary<string, string[]> PermissionsByEnvironmentName = new()
    {
        [EnvironmentNames.Dev] = [PermissionCodes.DeploymentsDeployDev],
        [EnvironmentNames.Qa] =
        [
            PermissionCodes.DeploymentsPromoteQa, PermissionCodes.DeploymentsApproveQa, PermissionCodes.DeploymentsDeployQa,
        ],
        [EnvironmentNames.Uat] =
        [
            PermissionCodes.DeploymentsPromoteUat, PermissionCodes.DeploymentsApproveUat, PermissionCodes.DeploymentsDeployUat,
        ],
        [EnvironmentNames.Production] =
        [
            PermissionCodes.DeploymentsPromoteProduction, PermissionCodes.DeploymentsDeployProduction,
        ],
    };

    public static async Task<(IReadOnlyList<string> Roles, IReadOnlyList<string> Permissions)> GetRolesAndPermissionsAsync(
        this IAppDbContext db, Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (user is null)
            return (Array.Empty<string>(), Array.Empty<string>());

        if (user.IsAdmin)
            return (["Admin"], PermissionCodes.All.Select(p => p.Code).ToList());

        var environmentNames = await db.UserEnvironmentAccess
            .Where(a => a.UserId == userId)
            .Select(a => a.EnvironmentDefinition.Name)
            .ToListAsync(cancellationToken);

        var permissions = new HashSet<string>();
        if (environmentNames.Count > 0)
            foreach (var p in BaseGrantedWithAnyEnvironmentAccess) permissions.Add(p);

        foreach (var envName in environmentNames)
        {
            if (PermissionsByEnvironmentName.TryGetValue(envName, out var envPermissions))
                foreach (var p in envPermissions) permissions.Add(p);
        }

        if (user.CanApproveProduction)
            permissions.Add(PermissionCodes.DeploymentsApproveProduction);

        var roles = new List<string>(environmentNames);
        if (user.CanApproveProduction)
            roles.Add("Production Approver");
        if (roles.Count == 0)
            roles.Add("No access");

        return (roles, permissions.ToList());
    }

    /// <summary>Per-specific-environment authorization check (master requirements
    /// §2/§12: "A user without QA access must receive 403 when calling QA APIs
    /// directly", enforced server-side, never just by hiding UI buttons). The
    /// coarse permission claims above (ContainersView, DeploymentsView, etc.) only
    /// establish that a user has access to AT LEAST ONE environment — callers that
    /// act on a specific ApplicationEnvironment/EnvironmentDefinition must also
    /// call this to confirm the user is allowed to see or act on THAT particular
    /// environment. Admins always pass.</summary>
    public static async Task<bool> HasEnvironmentAccessAsync(
        this IAppDbContext db, Guid userId, Guid environmentDefinitionId, CancellationToken cancellationToken = default)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (user is null)
            return false;
        if (user.IsAdmin)
            return true;

        return await db.UserEnvironmentAccess.AnyAsync(
            a => a.UserId == userId && a.EnvironmentDefinitionId == environmentDefinitionId, cancellationToken);
    }

    /// <summary>The set of EnvironmentDefinitionIds a user is allowed to see/act
    /// on — every environment for an admin, otherwise exactly their granted
    /// UserEnvironmentAccess rows. Used to scope a list/query down to only what
    /// the caller is authorized to see, rather than trusting caller-supplied
    /// filters (which a QA-only user could otherwise omit to see everything).</summary>
    public static async Task<IReadOnlyList<Guid>?> GetAccessibleEnvironmentIdsAsync(
        this IAppDbContext db, Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (user is null)
            return [];
        if (user.IsAdmin)
            return null; // null = no restriction (every environment)

        return await db.UserEnvironmentAccess.Where(a => a.UserId == userId).Select(a => a.EnvironmentDefinitionId).ToListAsync(cancellationToken);
    }
}
