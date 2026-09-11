using DevOpsPortal.Application.Common;
using DevOpsPortal.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace DevOpsPortal.Infrastructure.Persistence;

/// <summary>
/// Single-organization platform (Phase 12 removed the earlier multi-tenant
/// model — see PROJECT_STATE.md). No ambient tenant context, no query
/// filters — every row is simply visible to every authenticated user whose
/// permissions allow it, exactly like any single-tenant application.
/// </summary>
public class AppDbContext(DbContextOptions<AppDbContext> options)
    : DbContext(options), IAppDbContext
{
    public DbSet<User> Users => Set<User>();
    public DbSet<UserEnvironmentAccess> UserEnvironmentAccess => Set<UserEnvironmentAccess>();
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
        modelBuilder.Entity<User>(b =>
        {
            b.HasIndex(u => u.Username).IsUnique();
            b.HasIndex(u => u.Email).IsUnique();
            b.Property(u => u.Username).HasMaxLength(100).IsRequired();
            b.Property(u => u.Email).HasMaxLength(256).IsRequired();
            b.Property(u => u.FullName).HasMaxLength(200).IsRequired();
            b.Property(u => u.PasswordHash).IsRequired();
        });

        modelBuilder.Entity<UserEnvironmentAccess>(b =>
        {
            b.HasIndex(a => new { a.UserId, a.EnvironmentDefinitionId }).IsUnique();
            b.HasOne(a => a.User).WithMany(u => u.EnvironmentAccess)
                .HasForeignKey(a => a.UserId).OnDelete(DeleteBehavior.Cascade);
            b.HasOne(a => a.EnvironmentDefinition).WithMany()
                .HasForeignKey(a => a.EnvironmentDefinitionId).OnDelete(DeleteBehavior.Cascade);
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
            b.Property(r => r.DefaultBranch).HasMaxLength(200);
            b.Property(r => r.Username).HasMaxLength(200);
            b.Property(r => r.AccessTokenStoreKey).HasMaxLength(100);
            b.Property(r => r.AccessTokenEnvVarName).HasMaxLength(100);
        });

        modelBuilder.Entity<ManagedApplication>(b =>
        {
            b.HasIndex(a => a.Slug).IsUnique();
            b.Property(a => a.Name).HasMaxLength(200).IsRequired();
            b.Property(a => a.Slug).HasMaxLength(100).IsRequired();
            b.HasOne(a => a.Repository).WithMany()
                .HasForeignKey(a => a.RepositoryId).OnDelete(DeleteBehavior.SetNull);
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
            b.Property(s => s.Hostname).HasMaxLength(255);
            b.Property(s => s.SshUsername).HasMaxLength(100);
            b.Property(s => s.SshCredentialStoreKey).HasMaxLength(100);
            b.Property(s => s.SshPassphraseStoreKey).HasMaxLength(100);
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

        modelBuilder.Entity<BuildConfiguration>(b =>
        {
            b.HasIndex(bc => bc.ApplicationId).IsUnique();
            b.HasOne(bc => bc.Application).WithMany()
                .HasForeignKey(bc => bc.ApplicationId).OnDelete(DeleteBehavior.Cascade);
            b.HasOne(bc => bc.BuildServer).WithMany()
                .HasForeignKey(bc => bc.BuildServerId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<BuildServer>(b =>
        {
            b.HasIndex(s => s.Name).IsUnique();
            b.Property(s => s.Name).HasMaxLength(200).IsRequired();
            b.Property(s => s.BaseUrl).HasMaxLength(500).IsRequired();
            b.Property(s => s.ApiTokenEnvVarName).HasMaxLength(100);
        });

        modelBuilder.Entity<BuildRequest>(b =>
        {
            b.HasIndex(br => new { br.ApplicationId, br.RequestedAt });
            b.Property(br => br.JobName).HasMaxLength(500).IsRequired();
            b.HasOne(br => br.Application).WithMany()
                .HasForeignKey(br => br.ApplicationId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne(br => br.BuildServer).WithMany()
                .HasForeignKey(br => br.BuildServerId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Release>(b =>
        {
            b.HasIndex(r => r.BuildRequestId).IsUnique();
            b.Property(r => r.CommitSha).HasMaxLength(64).IsRequired();
            b.Property(r => r.ImageReference).HasMaxLength(1000).IsRequired();
            b.HasOne(r => r.Application).WithMany()
                .HasForeignKey(r => r.ApplicationId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne(r => r.BuildRequest).WithOne(br => br.Release)
                .HasForeignKey<Release>(r => r.BuildRequestId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SecretReference>(b =>
        {
            // No DB-level unique index here — SQL NULL comparison semantics don't
            // give the right uniqueness behavior across the nullable Application/
            // EnvironmentDefinition columns; SecretReferenceService enforces
            // uniqueness explicitly instead. See that class's CreateAsync.
            b.HasIndex(s => new { s.ApplicationId, s.EnvironmentDefinitionId });
            b.Property(s => s.Name).HasMaxLength(200).IsRequired();
            b.Property(s => s.Username).HasMaxLength(200);
            b.Property(s => s.Host).HasMaxLength(255);
            b.Property(s => s.DatabaseName).HasMaxLength(200);
            b.Property(s => s.ProviderKey).HasMaxLength(100).IsRequired();
            b.Property(s => s.StoreKey).HasMaxLength(100).IsRequired();
            b.HasOne(s => s.Application).WithMany()
                .HasForeignKey(s => s.ApplicationId).OnDelete(DeleteBehavior.Cascade);
            b.HasOne(s => s.EnvironmentDefinition).WithMany()
                .HasForeignKey(s => s.EnvironmentDefinitionId).OnDelete(DeleteBehavior.Restrict);
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
        });

        modelBuilder.Entity<DeploymentLogEntry>(b =>
        {
            b.HasIndex(l => new { l.DeploymentId, l.Sequence }).IsUnique();
            b.Property(l => l.Message).IsRequired();
            b.HasOne(l => l.Deployment).WithMany(d => d.LogEntries)
                .HasForeignKey(l => l.DeploymentId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PromotionRequest>(b =>
        {
            b.Property(p => p.CommitSha).HasMaxLength(64).IsRequired();
            b.Property(p => p.ApprovalTokenHash).HasMaxLength(100).IsRequired();
            b.Property(p => p.FromBranch).HasMaxLength(200);
            b.Property(p => p.ToBranch).HasMaxLength(200);
            b.Property(p => p.BranchPromotionDetail).HasMaxLength(500);
            b.HasIndex(p => new { p.ApplicationId, p.ToEnvironmentDefinitionId, p.Status });
            b.HasIndex(p => p.ApprovalTokenHash).IsUnique();
            b.HasOne(p => p.Application).WithMany()
                .HasForeignKey(p => p.ApplicationId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne(p => p.FromEnvironmentDefinition).WithMany()
                .HasForeignKey(p => p.FromEnvironmentDefinitionId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne(p => p.ToEnvironmentDefinition).WithMany()
                .HasForeignKey(p => p.ToEnvironmentDefinitionId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne(p => p.SourceDeployment).WithMany()
                .HasForeignKey(p => p.SourceDeploymentId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ProductionApproval>(b =>
        {
            b.HasIndex(a => a.PromotionRequestId).IsUnique();
            b.HasIndex(a => a.ApprovalTokenHash).IsUnique();
            b.Property(a => a.ApprovalTokenHash).HasMaxLength(100).IsRequired();
            b.HasOne(a => a.PromotionRequest).WithOne(p => p.ProductionApproval)
                .HasForeignKey<ProductionApproval>(a => a.PromotionRequestId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}
