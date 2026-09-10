using DevOpsPortal.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace DevOpsPortal.Application.Common;

/// <summary>
/// Persistence abstraction consumed by Application services. Implemented by the
/// EF Core DbContext in Infrastructure — keeps Application free of provider details.
/// </summary>
public interface IAppDbContext
{
    DbSet<User> Users { get; }
    DbSet<Role> Roles { get; }
    DbSet<Permission> Permissions { get; }
    DbSet<UserRole> UserRoles { get; }
    DbSet<RolePermission> RolePermissions { get; }
    DbSet<AuditLog> AuditLogs { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
