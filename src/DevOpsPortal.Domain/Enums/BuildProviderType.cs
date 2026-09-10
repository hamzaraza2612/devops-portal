namespace DevOpsPortal.Domain.Enums;

/// <summary>Which external build system a <see cref="Entities.BuildServer"/> talks
/// to. Deliberately an enum, not a free-form string, so the set of supported
/// providers is closed and every IBuildProvider implementation declares which
/// value it handles — see master requirements §1 ("do not tightly couple the
/// deployment engine to Jenkins").</summary>
public enum BuildProviderType
{
    Jenkins = 0,
}
