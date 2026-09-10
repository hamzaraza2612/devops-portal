using DevOpsPortal.Application.Dtos.Audit;
using DevOpsPortal.Domain.Entities;

namespace DevOpsPortal.Application.Services;

public interface IAuditService
{
    /// <summary>
    /// Records an audit entry. Actor defaults to the current authenticated caller;
    /// pass explicit actorUserId/actorUsername for events where the caller isn't
    /// authenticated yet (e.g. a login attempt).
    /// </summary>
    Task LogAsync(
        string action,
        AuditResult result,
        string? entityType = null,
        string? entityId = null,
        string? details = null,
        Guid? actorUserId = null,
        string? actorUsername = null,
        CancellationToken cancellationToken = default);

    Task<PagedResult<AuditLogDto>> QueryAsync(AuditQuery query, CancellationToken cancellationToken = default);
}
