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

    public static readonly IReadOnlyList<(string Code, string Description)> All = new (string, string)[]
    {
        (UsersView, "View users"),
        (UsersManage, "Create, update, deactivate users and assign roles"),
        (RolesView, "View roles and permissions"),
        (RolesManage, "Manage roles and role-permission assignments"),
        (AuditView, "View audit logs"),
    };
}
