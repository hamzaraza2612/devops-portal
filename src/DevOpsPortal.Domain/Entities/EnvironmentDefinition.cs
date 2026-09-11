namespace DevOpsPortal.Domain.Entities;

/// <summary>
/// A pipeline stage (DEV/QA/UAT/PRODUCTION) — seeded reference data, analogous
/// to Role. Named "Definition" to avoid clashing with ApplicationEnvironment,
/// which is the per-app instance of one of these.
/// </summary>
public class EnvironmentDefinition
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;

    /// <summary>Pipeline position (DEV &lt; QA &lt; UAT &lt; PRODUCTION); lower deploys first.</summary>
    public int SortOrder { get; set; }

    /// <summary>Flags environments (typically PRODUCTION) that warrant extra safeguards in later phases.</summary>
    public bool IsProductionLike { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>The TargetServer the Environment Infrastructure Dashboard SSHes to
    /// for this pipeline stage's real Docker discovery/monitoring — independent
    /// of any ApplicationEnvironment configuration, so infrastructure discovery
    /// works even before a single application is configured for this
    /// environment. Null means discovery isn't configured yet for this stage
    /// (the dashboard reports that honestly rather than guessing a server).</summary>
    public Guid? PrimaryTargetServerId { get; set; }
    public TargetServer? PrimaryTargetServer { get; set; }
}
