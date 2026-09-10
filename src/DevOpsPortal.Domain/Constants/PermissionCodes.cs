namespace DevOpsPortal.Domain.Constants;

/// <summary>
/// Well-known permission codes. New phases add new codes here rather than
/// hard-coding role checks — authorization always goes through permissions.
/// </summary>
public static class PermissionCodes
{
    public const string UsersView = "users.view";
    public const string UsersManage = "users.manage";
    public const string RolesView = "roles.view";
    public const string RolesManage = "roles.manage";
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
    public const string DeploymentsRollback = "deployments.rollback";

    public const string ContainersView = "containers.view";
    public const string ContainersControl = "containers.control";
    public const string ContainersRecreate = "containers.recreate";

    public static readonly IReadOnlyList<(string Code, string Description)> All = new (string, string)[]
    {
        (UsersView, "View users"),
        (UsersManage, "Create, update, deactivate users and assign roles"),
        (RolesView, "View roles and permissions"),
        (RolesManage, "Manage roles and role-permission assignments"),
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
        (DeploymentsRollback, "Roll an environment back to a previous successful deployment"),
        (ContainersView, "View live container status and health for configured application environments"),
        (ContainersControl, "Restart, start, or stop the containers of a configured application environment"),
        (ContainersRecreate, "Recreate a configured application environment's containers with docker compose down -v / up -d (destroys volumes)"),
    };
}
