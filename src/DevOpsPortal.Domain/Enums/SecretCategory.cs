namespace DevOpsPortal.Domain.Enums;

/// <summary>Classification only — informational for filtering/UI, not a
/// binding to a specific entity (e.g. a Registry-category secret is not
/// structurally tied to a particular BuildConfiguration row). See master
/// requirements §1 for the full list this enum covers.</summary>
public enum SecretCategory
{
    Database = 0,
    Api = 1,
    Registry = 2,
    GitLab = 3,
    Jenkins = 4,
    Smtp = 5,
    Server = 6,
    Other = 7,
}
