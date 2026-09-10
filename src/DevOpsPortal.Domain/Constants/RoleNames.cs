namespace DevOpsPortal.Domain.Constants;

public static class RoleNames
{
    public const string Admin = "ADMIN";
    public const string DevOps = "DEVOPS";
    public const string Developer = "DEVELOPER";
    public const string Qa = "QA";
    public const string Uat = "UAT";
    public const string Cto = "CTO";

    public static readonly IReadOnlyList<string> All = new[] { Admin, DevOps, Developer, Qa, Uat, Cto };
}
