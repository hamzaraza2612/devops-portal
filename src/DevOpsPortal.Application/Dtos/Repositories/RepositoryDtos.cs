using DevOpsPortal.Domain.Enums;

namespace DevOpsPortal.Application.Dtos.Repositories;

public record RepositoryDto(
    Guid Id,
    string Name,
    string Url,
    RepositoryProvider Provider,
    string? Description,
    string? AccessTokenEnvVarName,
    bool IsActive,
    DateTimeOffset CreatedAt);

public record CreateRepositoryRequest(string Name, string Url, RepositoryProvider Provider, string? Description, string? AccessTokenEnvVarName);

public record UpdateRepositoryRequest(
    string Name, string Url, RepositoryProvider Provider, string? Description, string? AccessTokenEnvVarName, bool IsActive);
