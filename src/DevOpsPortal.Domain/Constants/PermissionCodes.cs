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
    };
}
