namespace DevOpsPortal.Domain.Constants;

/// <summary>
/// Default, out-of-the-box permission grants for every non-admin role in
/// RoleNames.All (see master requirements §9). A tenant's ADMIN role always
/// gets every permission instead (see TenantService) — never listed here.
/// Single source of truth for both the historical global seed and
/// TenantService's per-tenant provisioning.
/// </summary>
public static class DefaultRolePermissions
{
    public static readonly IReadOnlyDictionary<string, string[]> Defaults = new Dictionary<string, string[]>
    {
        [RoleNames.Developer] =
        [
            PermissionCodes.ApplicationsView,
            PermissionCodes.EnvironmentsView,
            PermissionCodes.DeploymentsView,
            PermissionCodes.DeploymentsDeployDev,
            PermissionCodes.DeploymentsPromoteQa,
            PermissionCodes.ContainersView,
            PermissionCodes.BuildsView,
            PermissionCodes.BuildsRequest,
            PermissionCodes.SecretsView,
            PermissionCodes.SecretsReveal,
        ],
        [RoleNames.Qa] =
        [
            PermissionCodes.ApplicationsView,
            PermissionCodes.EnvironmentsView,
            PermissionCodes.DeploymentsView,
            PermissionCodes.DeploymentsApproveQa,
            PermissionCodes.DeploymentsDeployQa,
            PermissionCodes.ContainersView,
            PermissionCodes.BuildsView,
            PermissionCodes.SecretsView,
            PermissionCodes.SecretsReveal,
        ],
        [RoleNames.Uat] =
        [
            PermissionCodes.ApplicationsView,
            PermissionCodes.EnvironmentsView,
            PermissionCodes.DeploymentsView,
            PermissionCodes.DeploymentsApproveUat,
            PermissionCodes.DeploymentsDeployUat,
            PermissionCodes.ContainersView,
            PermissionCodes.BuildsView,
            PermissionCodes.SecretsView,
            PermissionCodes.SecretsReveal,
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
            PermissionCodes.BuildServersView,
            PermissionCodes.BuildServersManage,
            PermissionCodes.BuildsView,
            PermissionCodes.BuildsRequest,
            PermissionCodes.SecretsView,
            PermissionCodes.SecretsReveal,
            PermissionCodes.SecretsManage,
        ],
        [RoleNames.Cto] =
        [
            PermissionCodes.ApplicationsView,
            PermissionCodes.EnvironmentsView,
            PermissionCodes.DeploymentsView,
            PermissionCodes.DeploymentsApproveProduction,
            PermissionCodes.ContainersView,
            PermissionCodes.BuildsView,
        ],
    };
}
