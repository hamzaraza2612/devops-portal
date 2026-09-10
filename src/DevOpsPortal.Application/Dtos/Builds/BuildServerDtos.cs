using DevOpsPortal.Domain.Enums;

namespace DevOpsPortal.Application.Dtos.Builds;

/// <summary>Never carries the Jenkins API token — only ApiTokenEnvVarName, the name
/// of the environment variable holding it (see BuildServer.ApiTokenEnvVarName).</summary>
public record BuildServerDto(
    Guid Id,
    string Name,
    string? Description,
    BuildProviderType ProviderType,
    string BaseUrl,
    string? Username,
    string? ApiTokenEnvVarName,
    bool IsActive,
    DateTimeOffset CreatedAt);

public record CreateBuildServerRequest(
    string Name, string? Description, BuildProviderType ProviderType, string BaseUrl, string? Username, string? ApiTokenEnvVarName);

public record UpdateBuildServerRequest(
    string Name, string? Description, string BaseUrl, string? Username, string? ApiTokenEnvVarName, bool IsActive);
