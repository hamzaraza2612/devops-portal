namespace DevOpsPortal.Domain.Enums;

/// <summary>Configuration only in Phase 3 — no build/push execution exists yet
/// to actually apply this strategy. "Latest" is deliberately not an option:
/// never use `latest` as a deployment identity.</summary>
public enum ImageTagStrategy
{
    CommitSha = 0,
    BuildNumber = 1,
    SemVer = 2,
}
