namespace DevOpsPortal.Application.Dtos.TargetServers;

public record TargetServerDto(
    Guid Id,
    string Name,
    string? Description,
    string? Hostname,
    bool IsActive,
    DateTimeOffset CreatedAt,
    IReadOnlyList<AllowedDeploymentRootDto> AllowedDeploymentRoots);

public record CreateTargetServerRequest(string Name, string? Description, string? Hostname);

public record UpdateTargetServerRequest(string Name, string? Description, string? Hostname, bool IsActive);

public record AllowedDeploymentRootDto(Guid Id, Guid TargetServerId, string RootPath, string? Description, bool IsActive);

public record CreateAllowedDeploymentRootRequest(string RootPath, string? Description);

public record UpdateAllowedDeploymentRootRequest(string RootPath, string? Description, bool IsActive);
