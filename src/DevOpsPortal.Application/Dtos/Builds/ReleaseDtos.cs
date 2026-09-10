using DevOpsPortal.Domain.Enums;

namespace DevOpsPortal.Application.Dtos.Builds;

public record ReleaseDto(
    Guid Id,
    Guid ApplicationId,
    Guid BuildRequestId,
    string CommitSha,
    string? Branch,
    int BuildNumber,
    string ImageReference,
    BuildStatus BuildStatus,
    DateTimeOffset CreatedAt);
