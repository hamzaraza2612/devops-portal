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
    }
}
