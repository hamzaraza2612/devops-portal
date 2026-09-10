namespace DevOpsPortal.Domain.Constants;

/// <summary>Seeded pipeline stages — see master requirements §10. Additional
/// stages can be added later without code changes (EnvironmentDefinition
/// is a normal table); these four are the guaranteed baseline.</summary>
public static class EnvironmentNames
{
    public const string Dev = "DEV";
    public const string Qa = "QA";
    public const string Uat = "UAT";
    public const string Production = "PRODUCTION";

    public static readonly IReadOnlyList<(string Name, int SortOrder, bool IsProductionLike)> All = new (string, int, bool)[]
    {
        (Dev, 0, false),
        (Qa, 1, false),
        (Uat, 2, false),
        (Production, 3, true),
    };
}
