using DevOpsPortal.Application.Common;
using DevOpsPortal.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace DevOpsPortal.Infrastructure.Persistence;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options), IAppDbContext
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<UserRole> UserRoles => Set<UserRole>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<ManagedApplication> Applications => Set<ManagedApplication>();
    public DbSet<Repository> Repositories => Set<Repository>();
    public DbSet<EnvironmentDefinition> EnvironmentDefinitions => Set<EnvironmentDefinition>();
    public DbSet<TargetServer> TargetServers => Set<TargetServer>();
    public DbSet<AllowedDeploymentRoot> AllowedDeploymentRoots => Set<AllowedDeploymentRoot>();
    public DbSet<ApplicationEnvironment> ApplicationEnvironments => Set<ApplicationEnvironment>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(b =>
        {
            b.HasIndex(u => u.Username).IsUnique();
            b.HasIndex(u => u.Email).IsUnique();
            b.Property(u => u.Username).HasMaxLength(100).IsRequired();
            b.Property(u => u.Email).HasMaxLength(256).IsRequired();
            b.Property(u => u.FullName).HasMaxLength(200).IsRequired();
            b.Property(u => u.PasswordHash).IsRequired();
        });

        modelBuilder.Entity<Role>(b =>
        {
            b.HasIndex(r => r.Name).IsUnique();
            b.Property(r => r.Name).HasMaxLength(50).IsRequired();
        });

        modelBuilder.Entity<Permission>(b =>
        {
            b.HasIndex(p => p.Code).IsUnique();
            b.Property(p => p.Code).HasMaxLength(100).IsRequired();
        });

        modelBuilder.Entity<UserRole>(b =>
        {
            b.HasKey(ur => new { ur.UserId, ur.RoleId });
            b.HasOne(ur => ur.User).WithMany(u => u.UserRoles).HasForeignKey(ur => ur.UserId).OnDelete(DeleteBehavior.Cascade);
            b.HasOne(ur => ur.Role).WithMany(r => r.UserRoles).HasForeignKey(ur => ur.RoleId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<RolePermission>(b =>
        {
            b.HasKey(rp => new { rp.RoleId, rp.PermissionId });
            b.HasOne(rp => rp.Role).WithMany(r => r.RolePermissions).HasForeignKey(rp => rp.RoleId).OnDelete(DeleteBehavior.Cascade);
            b.HasOne(rp => rp.Permission).WithMany(p => p.RolePermissions).HasForeignKey(rp => rp.PermissionId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<AuditLog>(b =>
        {
            b.HasIndex(a => a.Timestamp);
            b.HasIndex(a => a.UserId);
            b.HasIndex(a => a.Action);
        });

        modelBuilder.Entity<Repository>(b =>
        {
            b.HasIndex(r => r.Name).IsUnique();
            b.Property(r => r.Name).HasMaxLength(200).IsRequired();
            b.Property(r => r.Url).HasMaxLength(1000).IsRequired();
        });

        modelBuilder.Entity<ManagedApplication>(b =>
        {
            b.HasIndex(a => a.Slug).IsUnique();
            b.Property(a => a.Name).HasMaxLength(200).IsRequired();
            b.Property(a => a.Slug).HasMaxLength(100).IsRequired();
            b.HasOne(a => a.Repository).WithMany().HasForeignKey(a => a.RepositoryId).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<EnvironmentDefinition>(b =>
        {
            b.HasIndex(e => e.Name).IsUnique();
            b.Property(e => e.Name).HasMaxLength(50).IsRequired();
        });

        modelBuilder.Entity<TargetServer>(b =>
        {
            b.HasIndex(s => s.Name).IsUnique();
            b.Property(s => s.Name).HasMaxLength(200).IsRequired();
        });

        modelBuilder.Entity<AllowedDeploymentRoot>(b =>
        {
            b.HasIndex(r => new { r.TargetServerId, r.RootPath }).IsUnique();
            b.Property(r => r.RootPath).HasMaxLength(500).IsRequired();
            b.HasOne(r => r.TargetServer).WithMany(s => s.AllowedDeploymentRoots)
                .HasForeignKey(r => r.TargetServerId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ApplicationEnvironment>(b =>
        {
            b.HasIndex(ae => new { ae.ApplicationId, ae.EnvironmentDefinitionId }).IsUnique();
            b.HasOne(ae => ae.Application).WithMany(a => a.Environments)
                .HasForeignKey(ae => ae.ApplicationId).OnDelete(DeleteBehavior.Cascade);
            b.HasOne(ae => ae.EnvironmentDefinition).WithMany()
                .HasForeignKey(ae => ae.EnvironmentDefinitionId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne(ae => ae.TargetServer).WithMany()
                .HasForeignKey(ae => ae.TargetServerId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
