using DevOpsPortal.Domain.Enums;

namespace DevOpsPortal.Application.Dtos.Repositories;

public record RepositoryDto(
    Guid Id,
    string Name,
    string Url,
    RepositoryProvider Provider,
    string? Description,
    bool IsActive,
    DateTimeOffset CreatedAt);

public record CreateRepositoryRequest(string Name, string Url, RepositoryProvider Provider, string? Description);

public record UpdateRepositoryRequest(string Name, string Url, RepositoryProvider Provider, string? Description, bool IsActive);
