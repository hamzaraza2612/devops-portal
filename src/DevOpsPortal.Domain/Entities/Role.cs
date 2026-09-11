namespace DevOpsPortal.Domain.Entities;

public class Role
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Null means a system-wide role (e.g. PLATFORM_ADMIN) shared across all tenants
    /// rather than owned by one. Non-null tenant-scoped roles are cloned from the system
    /// defaults into a new tenant at provisioning time (see TenantProvisioningService).</summary>
    public Guid? TenantId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool IsSystem { get; set; }

    public ICollection<UserRole> UserRoles { get; set; } = new List<UserRole>();
    public ICollection<RolePermission> RolePermissions { get; set; } = new List<RolePermission>();
}
