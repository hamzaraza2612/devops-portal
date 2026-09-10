using DevOpsPortal.Domain.Entities;

namespace DevOpsPortal.Application.Dtos.Audit;

public record AuditLogDto(
    Guid Id,
    DateTimeOffset Timestamp,
    Guid? UserId,
    string? Username,
    string Action,
    string? EntityType,
    string? EntityId,
    string? IpAddress,
    AuditResult Result,
    string? Details);

public record AuditQuery(
    Guid? UserId = null,
    string? Action = null,
    DateTimeOffset? From = null,
    DateTimeOffset? To = null,
    int Page = 1,
    int PageSize = 50);

public record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount);
