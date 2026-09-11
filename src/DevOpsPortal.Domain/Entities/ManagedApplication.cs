using DevOpsPortal.Domain.Enums;

namespace DevOpsPortal.Domain.Entities;

/// <summary>
/// A deployable application. Named "ManagedApplication" (not "Application") to
/// avoid colliding with the DevOpsPortal.Application project/namespace.
/// Deliberately holds no Techbey-specific fields — every environment-specific
/// detail lives on <see cref="ApplicationEnvironment"/>.
/// </summary>
public class ManagedApplication
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public string Name { get; set; } = string.Empty;

    /// <summary>Stable, URL-safe identifier (e.g. "dms-api"), independent of Name.</summary>
    public string Slug { get; set; } = string.Empty;

    public string? Description { get; set; }
    public DeploymentMode DeploymentMode { get; set; } = DeploymentMode.LegacyFilesystem;

    public Guid? RepositoryId { get; set; }
    public Repository? Repository { get; set; }

    /// <summary>Subdirectory within the repository holding this app's source (monorepo support). Null = repo root.</summary>
    public string? SourcePath { get; set; }

    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAt { get; set; }

    public ICollection<ApplicationEnvironment> Environments { get; set; } = new List<ApplicationEnvironment>();
}
