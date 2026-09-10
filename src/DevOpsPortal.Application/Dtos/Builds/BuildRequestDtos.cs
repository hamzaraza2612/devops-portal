using DevOpsPortal.Domain.Enums;

namespace DevOpsPortal.Application.Dtos.Builds;

public record BuildRequestDto(
    Guid Id,
    Guid ApplicationId,
    string ApplicationName,
    Guid BuildServerId,
    string BuildServerName,
    BuildProviderType ProviderType,
    string JobName,
    string? Branch,
    string? CommitSha,
    BuildStatus Status,
    string? ProviderQueueItemId,
    int? BuildNumber,
    string? BuildUrl,
    string? ErrorMessage,
    Guid RequestedByUserId,
    string? RequestedByUsername,
    DateTimeOffset RequestedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    Guid? ReleaseId,
    string? ImageReference);

/// <summary>SemVer is only required when the application's BuildConfiguration uses
/// ImageTagStrategy.SemVer — there is no external version source this portal can
/// derive one from, so the caller must supply it explicitly for that strategy;
/// omitting it for CommitSha/BuildNumber strategies is normal and expected.</summary>
public record RequestBuildRequest(string? Branch, string? CommitSha, string? SemVer);

public record BuildLogDto(bool Available, string? LogText, bool IsComplete, string? Message);
