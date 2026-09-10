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
    DbSet<ManagedApplication> Applications { get; }
    DbSet<Repository> Repositories { get; }
    DbSet<EnvironmentDefinition> EnvironmentDefinitions { get; }
    DbSet<TargetServer> TargetServers { get; }
    DbSet<AllowedDeploymentRoot> AllowedDeploymentRoots { get; }
    DbSet<ApplicationEnvironment> ApplicationEnvironments { get; }
    DbSet<BuildConfiguration> BuildConfigurations { get; }
    DbSet<Deployment> Deployments { get; }
    DbSet<DeploymentLogEntry> DeploymentLogEntries { get; }
    DbSet<PromotionRequest> PromotionRequests { get; }
    DbSet<ProductionApproval> ProductionApprovals { get; }
    DbSet<BuildServer> BuildServers { get; }
    DbSet<BuildRequest> BuildRequests { get; }
    DbSet<Release> Releases { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
