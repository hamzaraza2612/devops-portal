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

/// <summary>One top-level folder found while scanning a repository for
/// "Discover applications" (monorepo-style layout — one folder per
/// application, e.g. the Techbey techbey-apps/techbey-apps8 transition).
/// <c>ExistingApplicationId</c>/<c>ExistingApplicationName</c> are set when a
/// ManagedApplication already exists with this exact (RepositoryId,
/// SourcePath) pair — shown as "already linked" rather than offered again as
/// a new candidate.</summary>
public record DiscoveredRepositoryFolderDto(
    string Path, bool HasComposeFile, Guid? ExistingApplicationId, string? ExistingApplicationName);

public record DiscoverApplicationsResultDto(
    bool Success, string? Branch, IReadOnlyList<DiscoveredRepositoryFolderDto> Folders, string? ErrorMessage);

/// <summary>Every branch on a repository — lets an admin pick a real branch
/// from a live list (e.g. when configuring which branch an environment
/// deploys) instead of typing a branch name by hand and hoping it exists.</summary>
public record RepositoryBranchesResultDto(bool Success, IReadOnlyList<string> Branches, string? ErrorMessage);
