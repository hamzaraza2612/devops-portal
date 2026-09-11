using DevOpsPortal.Domain.Enums;

namespace DevOpsPortal.Domain.Entities;

/// <summary>
/// One line of a deployment's execution log. Modeled as rows (not a single
/// text blob) so this can move to centralized logging later without
/// redesigning the deployment domain — a future log shipper just tails this
/// table. Message must already be sanitized before it is written here (see
/// LogSanitizer) — never persist secrets.
/// </summary>
public class DeploymentLogEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }

    public Guid DeploymentId { get; set; }
    public Deployment Deployment { get; set; } = null!;

    /// <summary>Monotonic per-deployment ordering (timestamps alone can collide at high resolution).</summary>
    public int Sequence { get; set; }

    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
    public DeploymentLogLevel Level { get; set; } = DeploymentLogLevel.Info;
    public string Message { get; set; } = string.Empty;
}
