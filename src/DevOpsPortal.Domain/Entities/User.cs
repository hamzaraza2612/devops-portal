namespace DevOpsPortal.Domain.Entities;

/// <summary>
/// Authorization is deliberately simple (Phase 12 — replaced the earlier
/// Role/Permission/multi-tenant model): every user is either an
/// administrator (full access to everything) or holds explicit access to
/// one or more environments (see <see cref="UserEnvironmentAccess"/>).
/// <see cref="CanApproveProduction"/> is a separate flag from environment
/// access — approving a Production promotion (the "CTO" decision) and
/// deploying to Production are always two different grants, even for the
/// same person, matching the workflow's own long-standing invariant that
/// approval never implies deploy.
/// </summary>
public class User
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Username { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;

    /// <summary>Full access to everything — every environment, every admin screen
    /// (servers, repositories, GitLab, credentials, user management). Equivalent to
    /// the old ADMIN/DEVOPS roles combined.</summary>
    public bool IsAdmin { get; set; }

    /// <summary>Can decide (approve/reject) a Production promotion request — the "CTO"
    /// decision. Deliberately independent of both IsAdmin and Production environment
    /// access: granting this alone lets someone approve Production without being able
    /// to deploy it themselves, matching the pre-existing CTO-role behavior.</summary>
    public bool CanApproveProduction { get; set; }

    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastLoginAt { get; set; }

    public ICollection<UserEnvironmentAccess> EnvironmentAccess { get; set; } = new List<UserEnvironmentAccess>();
}
