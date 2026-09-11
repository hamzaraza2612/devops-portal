using DevOpsPortal.Application.Abstractions;
using DevOpsPortal.Application.Common;
using DevOpsPortal.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace DevOpsPortal.Infrastructure.Persistence;

/// <summary>
/// currentTenantService is captured (as an implicit field, via the primary
/// constructor) and referenced directly inside the HasQueryFilter lambdas
/// below — EF Core re-evaluates currentTenantService.TenantId per query, so
/// every tenant-owned entity is transparently scoped to whichever tenant is
/// ambient in this DbContext instance's DI scope (see ICurrentTenantService).
/// </summary>
public class AppDbContext(DbContextOptions<AppDbContext> options, ICurrentTenantService currentTenantService)
    : DbContext(options), IAppDbContext
{
    public DbSet<Tenant> Tenants => Set<Tenant>();
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
    public DbSet<BuildConfiguration> BuildConfigurations => Set<BuildConfiguration>();
    public DbSet<Deployment> Deployments => Set<Deployment>();
    public DbSet<DeploymentLogEntry> DeploymentLogEntries => Set<DeploymentLogEntry>();
    public DbSet<PromotionRequest> PromotionRequests => Set<PromotionRequest>();
    public DbSet<ProductionApproval> ProductionApprovals => Set<ProductionApproval>();
    public DbSet<BuildServer> BuildServers => Set<BuildServer>();
    public DbSet<BuildRequest> BuildRequests => Set<BuildRequest>();
    public DbSet<Release> Releases => Set<Release>();
    public DbSet<SecretReference> SecretReferences => Set<SecretReference>();

    /// <summary>Deliberately NOT on IAppDbContext — see SecretValueRecord's own
    /// doc comment. Only EncryptedSecretProvider (constructed with this
    /// concrete AppDbContext, not the interface) can reach this DbSet.</summary>
    public DbSet<SecretValueRecord> SecretValues => Set<SecretValueRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Tenant>(b =>
        {
            b.HasIndex(t => t.Slug).IsUnique();
            b.Property(t => t.Name).HasMaxLength(200).IsRequired();
            b.Property(t => t.Slug).HasMaxLength(100).IsRequired();
        });

        modelBuilder.Entity<User>(b =>
        {
            // Username/Email are deliberately GLOBALLY unique (not per-tenant): login
            // looks a user up by username alone, before any tenant is known, so two
            // tenants sharing a username would make login ambiguous. Enforced globally
            // both here and in UserService (which queries with IgnoreQueryFilters).
            b.HasIndex(u => u.Username).IsUnique();
            b.HasIndex(u => u.Email).IsUnique();
            b.HasIndex(u => u.TenantId);
            b.Property(u => u.Username).HasMaxLength(100).IsRequired();
            b.Property(u => u.Email).HasMaxLength(256).IsRequired();
            b.Property(u => u.FullName).HasMaxLength(200).IsRequired();
            b.Property(u => u.PasswordHash).IsRequired();
            b.HasOne<Tenant>().WithMany().HasForeignKey(u => u.TenantId).OnDelete(DeleteBehavior.Restrict);
            // Null TenantId = platform administrator (visible only to other platform
            // administrators); non-null = visible only within that same tenant.
            b.HasQueryFilter(u => u.TenantId == currentTenantService.TenantId);
        });

        modelBuilder.Entity<Role>(b =>
        {
            b.HasIndex(r => new { r.TenantId, r.Name }).IsUnique();
            // Postgres treats NULL != NULL, so the composite index above alone would not
            // stop two system-wide (TenantId == null) roles from sharing a name — this
            // partial index closes that gap for the null-tenant rows specifically.
            b.HasIndex(r => r.Name).IsUnique().HasFilter("\"TenantId\" IS NULL");
            b.Property(r => r.Name).HasMaxLength(50).IsRequired();
            b.HasOne<Tenant>().WithMany().HasForeignKey(r => r.TenantId).OnDelete(DeleteBehavior.Restrict);
            b.HasQueryFilter(r => r.TenantId == currentTenantService.TenantId);
        });

        modelBuilder.Entity<Permission>(b =>
        {
            // Global catalog of capabilities — never tenant-owned, always visible.
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
            b.HasIndex(a => a.TenantId);
            b.HasOne<Tenant>().WithMany().HasForeignKey(a => a.TenantId).OnDelete(DeleteBehavior.Restrict);
            // Null TenantId events (platform-level actions) are only visible when no
            // tenant is ambient (a platform administrator); tenant events only within
            // that same tenant — same formula as every other tenant-owned entity.
            b.HasQueryFilter(a => a.TenantId == currentTenantService.TenantId);
        });

        modelBuilder.Entity<Repository>(b =>
        {
            b.HasIndex(r => new { r.TenantId, r.Name }).IsUnique();
            b.Property(r => r.Name).HasMaxLength(200).IsRequired();
            b.Property(r => r.Url).HasMaxLength(1000).IsRequired();
            b.Property(r => r.AccessTokenEnvVarName).HasMaxLength(100);
            b.HasOne<Tenant>().WithMany().HasForeignKey(r => r.TenantId).OnDelete(DeleteBehavior.Restrict);
            b.HasQueryFilter(r => r.TenantId == currentTenantService.TenantId);
        });

        modelBuilder.Entity<ManagedApplication>(b =>
        {
            b.HasIndex(a => new { a.TenantId, a.Slug }).IsUnique();
            b.Property(a => a.Name).HasMaxLength(200).IsRequired();
            b.Property(a => a.Slug).HasMaxLength(100).IsRequired();
            b.HasOne(a => a.Repository).WithMany().HasForeignKey(a => a.RepositoryId).OnDelete(DeleteBehavior.SetNull);
            b.HasOne<Tenant>().WithMany().HasForeignKey(a => a.TenantId).OnDelete(DeleteBehavior.Restrict);
            b.HasQueryFilter(a => a.TenantId == currentTenantService.TenantId);
        });

        modelBuilder.Entity<EnvironmentDefinition>(b =>
        {
            b.HasIndex(e => new { e.TenantId, e.Name }).IsUnique();
            b.Property(e => e.Name).HasMaxLength(50).IsRequired();
            b.HasOne<Tenant>().WithMany().HasForeignKey(e => e.TenantId).OnDelete(DeleteBehavior.Restrict);
            b.HasQueryFilter(e => e.TenantId == currentTenantService.TenantId);
        });

        modelBuilder.Entity<TargetServer>(b =>
        {
            b.HasIndex(s => new { s.TenantId, s.Name }).IsUnique();
            b.Property(s => s.Name).HasMaxLength(200).IsRequired();
            b.HasOne<Tenant>().WithMany().HasForeignKey(s => s.TenantId).OnDelete(DeleteBehavior.Restrict);
            b.HasQueryFilter(s => s.TenantId == currentTenantService.TenantId);
        });

        modelBuilder.Entity<AllowedDeploymentRoot>(b =>
        {
            b.HasIndex(r => new { r.TargetServerId, r.RootPath }).IsUnique();
            b.HasIndex(r => r.TenantId);
            b.Property(r => r.RootPath).HasMaxLength(500).IsRequired();
            b.HasOne(r => r.TargetServer).WithMany(s => s.AllowedDeploymentRoots)
                .HasForeignKey(r => r.TargetServerId).OnDelete(DeleteBehavior.Cascade);
            b.HasOne<Tenant>().WithMany().HasForeignKey(r => r.TenantId).OnDelete(DeleteBehavior.Restrict);
            b.HasQueryFilter(r => r.TenantId == currentTenantService.TenantId);
        });

        modelBuilder.Entity<ApplicationEnvironment>(b =>
        {
            b.HasIndex(ae => new { ae.ApplicationId, ae.EnvironmentDefinitionId }).IsUnique();
            b.HasIndex(ae => ae.TenantId);
            b.HasOne(ae => ae.Application).WithMany(a => a.Environments)
                .HasForeignKey(ae => ae.ApplicationId).OnDelete(DeleteBehavior.Cascade);
            b.HasOne(ae => ae.EnvironmentDefinition).WithMany()
                .HasForeignKey(ae => ae.EnvironmentDefinitionId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne(ae => ae.TargetServer).WithMany()
                .HasForeignKey(ae => ae.TargetServerId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<Tenant>().WithMany().HasForeignKey(ae => ae.TenantId).OnDelete(DeleteBehavior.Restrict);
            b.HasQueryFilter(ae => ae.TenantId == currentTenantService.TenantId);
        });

        modelBuilder.Entity<BuildConfiguration>(b =>
        {
            b.HasIndex(bc => bc.ApplicationId).IsUnique();
            b.HasIndex(bc => bc.TenantId);
            b.HasOne(bc => bc.Application).WithMany()
                .HasForeignKey(bc => bc.ApplicationId).OnDelete(DeleteBehavior.Cascade);
            b.HasOne(bc => bc.BuildServer).WithMany()
                .HasForeignKey(bc => bc.BuildServerId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<Tenant>().WithMany().HasForeignKey(bc => bc.TenantId).OnDelete(DeleteBehavior.Restrict);
            b.HasQueryFilter(bc => bc.TenantId == currentTenantService.TenantId);
        });

        modelBuilder.Entity<BuildServer>(b =>
        {
            b.HasIndex(s => new { s.TenantId, s.Name }).IsUnique();
            b.Property(s => s.Name).HasMaxLength(200).IsRequired();
            b.Property(s => s.BaseUrl).HasMaxLength(500).IsRequired();
            b.Property(s => s.ApiTokenEnvVarName).HasMaxLength(100);
            b.HasOne<Tenant>().WithMany().HasForeignKey(s => s.TenantId).OnDelete(DeleteBehavior.Restrict);
            b.HasQueryFilter(s => s.TenantId == currentTenantService.TenantId);
        });

        modelBuilder.Entity<BuildRequest>(b =>
        {
            b.HasIndex(br => new { br.ApplicationId, br.RequestedAt });
            b.HasIndex(br => br.TenantId);
            b.Property(br => br.JobName).HasMaxLength(500).IsRequired();
            b.HasOne(br => br.Application).WithMany()
                .HasForeignKey(br => br.ApplicationId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne(br => br.BuildServer).WithMany()
                .HasForeignKey(br => br.BuildServerId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<Tenant>().WithMany().HasForeignKey(br => br.TenantId).OnDelete(DeleteBehavior.Restrict);
            b.HasQueryFilter(br => br.TenantId == currentTenantService.TenantId);
        });

        modelBuilder.Entity<Release>(b =>
        {
            b.HasIndex(r => r.BuildRequestId).IsUnique();
            b.HasIndex(r => r.TenantId);
            b.Property(r => r.CommitSha).HasMaxLength(64).IsRequired();
            b.Property(r => r.ImageReference).HasMaxLength(1000).IsRequired();
            b.HasOne(r => r.Application).WithMany()
                .HasForeignKey(r => r.ApplicationId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne(r => r.BuildRequest).WithOne(br => br.Release)
                .HasForeignKey<Release>(r => r.BuildRequestId).OnDelete(DeleteBehavior.Cascade);
            b.HasOne<Tenant>().WithMany().HasForeignKey(r => r.TenantId).OnDelete(DeleteBehavior.Restrict);
            b.HasQueryFilter(r => r.TenantId == currentTenantService.TenantId);
        });

        modelBuilder.Entity<SecretReference>(b =>
        {
            // No DB-level unique index here — SQL NULL comparison semantics don't
            // give the right uniqueness behavior across the nullable Application/
            // EnvironmentDefinition columns; SecretReferenceService enforces
            // uniqueness explicitly instead. See that class's CreateAsync.
            b.HasIndex(s => new { s.ApplicationId, s.EnvironmentDefinitionId });
            b.HasIndex(s => s.TenantId);
            b.Property(s => s.Name).HasMaxLength(200).IsRequired();
            b.Property(s => s.ProviderKey).HasMaxLength(100).IsRequired();
            b.Property(s => s.StoreKey).HasMaxLength(100).IsRequired();
            b.HasOne(s => s.Application).WithMany()
                .HasForeignKey(s => s.ApplicationId).OnDelete(DeleteBehavior.Cascade);
            b.HasOne(s => s.EnvironmentDefinition).WithMany()
                .HasForeignKey(s => s.EnvironmentDefinitionId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<Tenant>().WithMany().HasForeignKey(s => s.TenantId).OnDelete(DeleteBehavior.Restrict);
            b.HasQueryFilter(s => s.TenantId == currentTenantService.TenantId);
        });

        modelBuilder.Entity<SecretValueRecord>(b =>
        {
            b.HasKey(v => v.StoreKey);
            b.Property(v => v.StoreKey).HasMaxLength(100);
            b.Property(v => v.Ciphertext).IsRequired();
            b.Property(v => v.Nonce).IsRequired();
            b.Property(v => v.Tag).IsRequired();
        });

        modelBuilder.Entity<Deployment>(b =>
        {
            b.Property(d => d.CommitSha).HasMaxLength(64).IsRequired();
            b.HasIndex(d => new { d.ApplicationId, d.EnvironmentDefinitionId, d.Status });
            b.HasIndex(d => new { d.ApplicationId, d.EnvironmentDefinitionId, d.CommitSha });
            b.HasIndex(d => d.TenantId);
            // DB-level concurrency safety net (Postgres partial unique index): at most one
            // active (Pending/Queued/Running = 0/1/2) deployment per (Application, Environment),
            // regardless of any application-level check-then-insert race. Application code also
            // checks this up front for a clean ConflictException in the common case.
            b.HasIndex(d => new { d.ApplicationId, d.EnvironmentDefinitionId })
                .HasFilter("\"Status\" IN (0,1,2)")
                .IsUnique();
            b.HasOne(d => d.Application).WithMany()
                .HasForeignKey(d => d.ApplicationId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne(d => d.EnvironmentDefinition).WithMany()
                .HasForeignKey(d => d.EnvironmentDefinitionId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne(d => d.ApplicationEnvironment).WithMany()
                .HasForeignKey(d => d.ApplicationEnvironmentId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne(d => d.RollbackOfDeployment).WithMany()
                .HasForeignKey(d => d.RollbackOfDeploymentId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne(d => d.PromotionRequest).WithMany()
                .HasForeignKey(d => d.PromotionRequestId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne(d => d.Release).WithMany()
                .HasForeignKey(d => d.ReleaseId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<Tenant>().WithMany().HasForeignKey(d => d.TenantId).OnDelete(DeleteBehavior.Restrict);
            b.HasQueryFilter(d => d.TenantId == currentTenantService.TenantId);
        });

        modelBuilder.Entity<DeploymentLogEntry>(b =>
        {
            b.HasIndex(l => new { l.DeploymentId, l.Sequence }).IsUnique();
            b.HasIndex(l => l.TenantId);
            b.Property(l => l.Message).IsRequired();
            b.HasOne(l => l.Deployment).WithMany(d => d.LogEntries)
                .HasForeignKey(l => l.DeploymentId).OnDelete(DeleteBehavior.Cascade);
            b.HasOne<Tenant>().WithMany().HasForeignKey(l => l.TenantId).OnDelete(DeleteBehavior.Restrict);
            b.HasQueryFilter(l => l.TenantId == currentTenantService.TenantId);
        });

        modelBuilder.Entity<PromotionRequest>(b =>
        {
            b.Property(p => p.CommitSha).HasMaxLength(64).IsRequired();
            b.Property(p => p.ApprovalTokenHash).HasMaxLength(100).IsRequired();
            b.HasIndex(p => new { p.ApplicationId, p.ToEnvironmentDefinitionId, p.Status });
            b.HasIndex(p => p.ApprovalTokenHash).IsUnique();
            b.HasIndex(p => p.TenantId);
            b.HasOne(p => p.Application).WithMany()
                .HasForeignKey(p => p.ApplicationId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne(p => p.FromEnvironmentDefinition).WithMany()
                .HasForeignKey(p => p.FromEnvironmentDefinitionId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne(p => p.ToEnvironmentDefinition).WithMany()
                .HasForeignKey(p => p.ToEnvironmentDefinitionId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne(p => p.SourceDeployment).WithMany()
                .HasForeignKey(p => p.SourceDeploymentId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<Tenant>().WithMany().HasForeignKey(p => p.TenantId).OnDelete(DeleteBehavior.Restrict);
            b.HasQueryFilter(p => p.TenantId == currentTenantService.TenantId);
        });

        modelBuilder.Entity<ProductionApproval>(b =>
        {
            b.HasIndex(a => a.PromotionRequestId).IsUnique();
            b.HasIndex(a => a.ApprovalTokenHash).IsUnique();
            b.HasIndex(a => a.TenantId);
            b.Property(a => a.ApprovalTokenHash).HasMaxLength(100).IsRequired();
            b.HasOne(a => a.PromotionRequest).WithOne(p => p.ProductionApproval)
                .HasForeignKey<ProductionApproval>(a => a.PromotionRequestId).OnDelete(DeleteBehavior.Cascade);
            b.HasOne<Tenant>().WithMany().HasForeignKey(a => a.TenantId).OnDelete(DeleteBehavior.Restrict);
            b.HasQueryFilter(a => a.TenantId == currentTenantService.TenantId);
        });
    }
}
