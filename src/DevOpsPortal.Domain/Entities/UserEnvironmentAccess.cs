namespace DevOpsPortal.Domain.Entities;

/// <summary>
/// Grants a user full capability (view, promote/request, approve, deploy,
/// rollback, container operations) within one environment tier — the whole
/// of the simplified Phase 12 authorization model for non-admin users. See
/// <see cref="User.IsAdmin"/> (bypasses this entirely) and
/// <see cref="User.CanApproveProduction"/> (the one capability deliberately
/// kept separate from environment access).
/// </summary>
public class UserEnvironmentAccess
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid UserId { get; set; }
    public User User { get; set; } = null!;

    public Guid EnvironmentDefinitionId { get; set; }
    public EnvironmentDefinition EnvironmentDefinition { get; set; } = null!;
}
