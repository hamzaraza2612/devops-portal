using DevOpsPortal.Application.Abstractions;
using DevOpsPortal.Application.Common;
using DevOpsPortal.Application.Dtos.Audit;
using DevOpsPortal.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace DevOpsPortal.Application.Services;

public class AuditService(IAppDbContext db, ICurrentUserService currentUser) : IAuditService
{
    public async Task LogAsync(
        string action,
        AuditResult result,
        string? entityType = null,
        string? entityId = null,
        string? details = null,
        Guid? actorUserId = null,
        string? actorUsername = null,
        CancellationToken cancellationToken = default)
    {
        var entry = new AuditLog
        {
            Action = action,
            Result = result,
            EntityType = entityType,
            EntityId = entityId,
            Details = details,
            UserId = actorUserId ?? currentUser.UserId,
            Username = actorUsername ?? currentUser.Username,
            IpAddress = currentUser.IpAddress,
        };

        db.AuditLogs.Add(entry);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<PagedResult<AuditLogDto>> QueryAsync(AuditQuery query, CancellationToken cancellationToken = default)
    {
        var q = db.AuditLogs.AsQueryable();

        if (query.UserId is not null)
            q = q.Where(a => a.UserId == query.UserId);
        if (!string.IsNullOrWhiteSpace(query.Action))
            q = q.Where(a => a.Action == query.Action);
        if (query.From is not null)
            q = q.Where(a => a.Timestamp >= query.From);
        if (query.To is not null)
            q = q.Where(a => a.Timestamp <= query.To);

        var total = await q.CountAsync(cancellationToken);

        var page = Math.Max(query.Page, 1);
        var pageSize = Math.Clamp(query.PageSize, 1, 200);

        var items = await q
            .OrderByDescending(a => a.Timestamp)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(a => new AuditLogDto(a.Id, a.Timestamp, a.UserId, a.Username, a.Action, a.EntityType, a.EntityId, a.IpAddress, a.Result, a.Details))
            .ToListAsync(cancellationToken);

        return new PagedResult<AuditLogDto>(items, page, pageSize, total);
    }
}
