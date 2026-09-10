namespace DevOpsPortal.Domain.Entities;

public class Permission
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Stable machine key, e.g. "users.manage". Referenced by authorization policies.</summary>
    public string Code { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    public ICollection<RolePermission> RolePermissions { get; set; } = new List<RolePermission>();
}
