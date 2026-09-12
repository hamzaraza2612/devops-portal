using DevOpsPortal.Domain.Enums;

namespace DevOpsPortal.Domain.Entities;

/// <summary>
/// Per-application, per-pipeline-stage configuration — the record that answers
/// "where and how does this app's DEV/QA/UAT/PRODUCTION instance run".
/// Deliberately flat/nullable rather than split by DeploymentMode: legacy
/// filesystem fields are unused (null) when DeploymentMode is ContainerImage.
/// Every field here varies per app per environment — never assume folder
/// name = compose service name = container name (master requirements §10).
/// </summary>
public class ApplicationEnvironment
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ApplicationId { get; set; }
    public ManagedApplication Application { get; set; } = null!;

    public Guid EnvironmentDefinitionId { get; set; }
    public EnvironmentDefinition EnvironmentDefinition { get; set; } = null!;

    public Guid TargetServerId { get; set; }
    public TargetServer TargetServer { get; set; } = null!;

    /// <summary>Branch deployed to this environment for this app (master requirements §7 — configurable per application).</summary>
    public string? BranchName { get; set; }

    // --- Legacy filesystem mode configuration ---

    /// <summary>Absolute path to the application's directory on the target server. Must resolve under one of the target server's AllowedDeploymentRoots.</summary>
    public string? DeploymentRootPath { get; set; }

    /// <summary>Subdirectory under DeploymentRootPath synced with the published build. Default "publish".</summary>
    public string PublishSubPath { get; set; } = "publish";

    /// <summary>Subdirectory under DeploymentRootPath holding rollback snapshots. Default "Backups".</summary>
    public string BackupSubPath { get; set; } = "Backups";

    /// <summary>How many backup snapshots to retain; null = no automatic pruning.</summary>
    public int? BackupRetentionCount { get; set; }

    /// <summary>Path to the docker-compose file, relative to DeploymentRootPath. Default "docker-compose.yml".</summary>
    public string ComposeFilePath { get; set; } = "docker-compose.yml";

    /// <summary>Explicit Compose project name override; null lets Compose default to the folder name.</summary>
    public string? ComposeProjectName { get; set; }

    /// <summary>Compose service key (services.&lt;name&gt;) — independent of Application.Slug and folder name.</summary>
    public string? ServiceName { get; set; }

    /// <summary>Explicit container_name override, if the compose file sets one.</summary>
    public string? ContainerName { get; set; }

    /// <summary>Pre-existing external Docker network this app's containers join, if any.</summary>
    public string? ExternalNetworkName { get; set; }

    /// <summary>When true, deployment execution runs `docker compose down -v` (destroys volumes)
    /// instead of a plain `down` before `up -d`. Per-app-environment opt-in only — never applied
    /// by default (master requirements §5: "Do NOT automatically apply down -v to every application").</summary>
    public bool UseDownWithVolumesOnDeploy { get; set; }

    /// <summary>When true, deployment execution first downloads the configured Repository's
    /// source archive for this deployment's branch/commit and syncs it into
    /// DeploymentRootPath/PublishSubPath on the target server, before the compose cycle runs
    /// — the "obtain/update source" step of the deployment flow. Requires Application.RepositoryId
    /// to be set (enforced by ApplicationEnvironmentService.UpsertAsync). Deliberately opt-in and
    /// off by default: an application whose files are already placed on the target server by an
    /// existing external mechanism (e.g. the Techbey techbey-apps/techbey-apps8 transition) keeps
    /// working completely unchanged unless this is explicitly turned on.</summary>
    public bool SyncSourceFromRepository { get; set; }

    // --- Health check configuration (config only — not probed in Phase 2) ---

    public HealthCheckType HealthCheckType { get; set; } = HealthCheckType.None;

    /// <summary>Absolute URL, e.g. "http://host:5555/health" (Http) or "host:port" (TcpPort);
    /// meaning depends on HealthCheckType. Must be independently reachable from wherever the
    /// deployment engine runs — this table has no separate host/port field of its own.</summary>
    public string? HealthCheckEndpoint { get; set; }

    public int HealthCheckIntervalSeconds { get; set; } = 30;
    public int HealthCheckTimeoutSeconds { get; set; } = 5;

    /// <summary>Optional browser-facing URL for this app in this environment (e.g.
    /// "https://dev.example.com/app") — purely informational, shown in the portal UI
    /// so a developer can open the running app without knowing the underlying host.
    /// Not used by the deployment engine itself. Never contains credentials (same
    /// validation as Repository.Url).</summary>
    public string? ApplicationUrl { get; set; }

    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAt { get; set; }
}
