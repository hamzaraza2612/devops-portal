namespace DevOpsPortal.Domain.Entities;

public enum AuditResult
{
    Success,
    Failure
}

public class AuditLog
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>"who" — null when the actor could not be identified (e.g. failed login).</summary>
    public Guid? UserId { get; set; }
    public string? Username { get; set; }

    /// <summary>"what", e.g. "auth.login", "user.create", "role.assign".</summary>
    public string Action { get; set; } = string.Empty;

    public string? EntityType { get; set; }
    public string? EntityId { get; set; }

    /// <summary>"where" — caller IP address.</summary>
    public string? IpAddress { get; set; }

    public AuditResult Result { get; set; }

    /// <summary>Free-form, non-secret context. Never store credentials/tokens here.</summary>
    public string? Details { get; set; }
}
