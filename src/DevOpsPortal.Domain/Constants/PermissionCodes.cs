namespace DevOpsPortal.Domain.Constants;

/// <summary>
/// Well-known permission codes checked via [RequirePermission] / EnsurePermissionAsync.
/// Phase 12 replaced the underlying Role/Permission catalog with a simplified
/// User.IsAdmin / User.CanApproveProduction / UserEnvironmentAccess model (see
/// PROJECT_STATE.md and AppDbContextExtensions.GetRolesAndPermissionsAsync), but
/// every enforcement point below is unchanged — these codes are still exactly
/// what gets checked, just synthesized differently at login time.
/// </summary>
public static class PermissionCodes
{
    public const string UsersView = "users.view";
    public const string UsersManage = "users.manage";
    public const string AuditView = "audit.view";
    public const string ApplicationsView = "applications.view";
    public const string ApplicationsManage = "applications.manage";
    public const string RepositoriesView = "repositories.view";
    public const string RepositoriesManage = "repositories.manage";
    public const string TargetServersView = "targetservers.view";
    public const string TargetServersManage = "targetservers.manage";
    public const string EnvironmentsView = "environments.view";

    public const string DeploymentsView = "deployments.view";
    public const string DeploymentsDeployDev = "deployments.deploy.dev";
    public const string DeploymentsPromoteQa = "deployments.promote.qa";
    public const string DeploymentsApproveQa = "deployments.approve.qa";
    public const string DeploymentsDeployQa = "deployments.deploy.qa";
    public const string DeploymentsPromoteUat = "deployments.promote.uat";
    public const string DeploymentsApproveUat = "deployments.approve.uat";
    public const string DeploymentsDeployUat = "deployments.deploy.uat";
    public const string DeploymentsPromoteProduction = "deployments.promote.production";
    public const string DeploymentsApproveProduction = "deployments.approve.production";
    public const string DeploymentsDeployProduction = "deployments.deploy.production";

    public const string ContainersView = "containers.view";
    public const string ContainersControl = "containers.control";
    public const string ContainersRecreate = "containers.recreate";

    public const string BuildServersView = "buildservers.view";
    public const string BuildServersManage = "buildservers.manage";
    public const string BuildsView = "builds.view";
    public const string BuildsRequest = "builds.request";

    public const string SecretsView = "secrets.view";
    public const string SecretsReveal = "secrets.reveal";
    public const string SecretsManage = "secrets.manage";

    public static readonly IReadOnlyList<(string Code, string Description)> All = new (string, string)[]
    {
        (UsersView, "View users"),
        (UsersManage, "Create, update, deactivate users and manage their environment access"),
        (AuditView, "View audit logs"),
        (ApplicationsView, "View applications and their environment configuration"),
        (ApplicationsManage, "Create and configure applications and their environments"),
        (RepositoriesView, "View repository references"),
        (RepositoriesManage, "Create and configure repository references"),
        (TargetServersView, "View target servers and their allowed deployment roots"),
        (TargetServersManage, "Create and configure target servers and allowed deployment roots"),
        (EnvironmentsView, "View pipeline environment definitions"),
        (DeploymentsView, "View deployments, promotion requests, and deployment logs"),
        (DeploymentsDeployDev, "Deploy an application to DEV"),
        (DeploymentsPromoteQa, "Request promotion of a successful DEV deployment to QA"),
        (DeploymentsApproveQa, "Approve a pending QA promotion request"),
        (DeploymentsDeployQa, "Deploy an approved promotion to QA"),
        (DeploymentsPromoteUat, "Request promotion of a successful QA deployment to UAT"),
        (DeploymentsApproveUat, "Approve a pending UAT promotion request"),
        (DeploymentsDeployUat, "Deploy an approved promotion to UAT"),
        (DeploymentsPromoteProduction, "Request promotion of a successful UAT deployment to Production"),
        (DeploymentsApproveProduction, "Grant CTO approval for a Production promotion request"),
        (DeploymentsDeployProduction, "Deploy an approved, CTO-approved promotion to Production"),
        (ContainersView, "View live container status and health for configured application environments"),
        (ContainersControl, "Restart, start, or stop the containers of a configured application environment"),
        (ContainersRecreate, "Recreate a configured application environment's containers with docker compose down -v / up -d (destroys volumes)"),
        (BuildServersView, "View configured build servers (e.g. Jenkins instances)"),
        (BuildServersManage, "Create and configure build servers"),
        (BuildsView, "View build requests, their status, logs, and resulting releases"),
        (BuildsRequest, "Request a build of an application from a configured build server"),
        (SecretsView, "View secret/credential reference metadata (never the value)"),
        (SecretsReveal, "Reveal a secret/credential's actual value via the controlled 'Show password' action"),
        (SecretsManage, "Create, rotate, and delete secret/credential references"),
    };
}
