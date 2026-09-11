using DevOpsPortal.Domain.Enums;

namespace DevOpsPortal.Application.Dtos.Repositories;

public record RepositoryDto(
    Guid Id,
    string Name,
    string Url,
    RepositoryProvider Provider,
    string? Description,
    string? DefaultBranch,
    string? Username,
    bool HasAccessToken,
    string? AccessTokenEnvVarName,
    bool IsActive,
    DateTimeOffset CreatedAt);

public record CreateRepositoryRequest(
    string Name, string Url, RepositoryProvider Provider, string? Description, string? DefaultBranch, string? Username, string? AccessTokenEnvVarName);

public record UpdateRepositoryRequest(
    string Name, string Url, RepositoryProvider Provider, string? Description, string? DefaultBranch, string? Username,
    string? AccessTokenEnvVarName, bool IsActive);

/// <summary>The GitLab access token/password value, stored via the same
/// encrypted secret store every other credential in the portal uses (never
/// returned by any API).</summary>
public record SetRepositoryAccessTokenRequest(string Value);

public record RepositoryConnectionTestResultDto(
    bool Connected, string? AuthenticatedAs, string? ProjectName, string? ErrorMessage, DateTimeOffset TestedAt);
