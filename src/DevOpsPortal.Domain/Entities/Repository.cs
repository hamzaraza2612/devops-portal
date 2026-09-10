using DevOpsPortal.Domain.Enums;

namespace DevOpsPortal.Domain.Entities;

/// <summary>
/// A Git repository reference. Holds no credentials (see master requirements
/// §14) — repository access secrets are a separate, later concern.
/// </summary>
public class Repository
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public RepositoryProvider Provider { get; set; } = RepositoryProvider.GitLab;
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
