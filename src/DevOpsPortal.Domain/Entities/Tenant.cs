namespace DevOpsPortal.Domain.Entities;

/// <summary>
/// An organization/customer using the platform. Every tenant-owned resource
/// (applications, repositories, environments, deployment targets, users,
/// roles, deployments, secrets, etc.) carries a TenantId that resolves back
/// to one of these rows, and is isolated from every other tenant's data via
/// EF Core global query filters (see AppDbContext). A null TenantId on User
/// or Role means "platform-level" (a platform administrator or system role)
/// rather than belonging to any one tenant.
/// </summary>
public class Tenant
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;

    /// <summary>Stable, URL-safe identifier (e.g. "acme-corp"), independent of Name.</summary>
    public string Slug { get; set; } = string.Empty;

    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAt { get; set; }
}
