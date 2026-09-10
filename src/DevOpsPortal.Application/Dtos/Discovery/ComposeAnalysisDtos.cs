namespace DevOpsPortal.Application.Dtos.Discovery;

public record ComposeAnalysisRequest(string Content);

/// <summary>Purely a parsed preview — nothing here is persisted automatically;
/// an admin reviews it and creates/updates the real config via the normal
/// Application/ApplicationEnvironment endpoints.</summary>
public record ComposeAnalysisResult(IReadOnlyList<ComposeServiceSummary> Services, IReadOnlyList<string> Warnings);

public record ComposeServiceSummary(
    string ServiceName,
    string? Image,
    string? ContainerName,
    IReadOnlyList<string> Ports,
    IReadOnlyList<ComposeVolumeSummary> Volumes,
    IReadOnlyList<string> EnvironmentEntries,
    string? WorkingDir,
    IReadOnlyList<string> Entrypoint,
    IReadOnlyList<string> ExtraHosts,
    IReadOnlyList<string> Networks,
    bool UsesExternalNetwork,
    string? RestartPolicy);

public record ComposeVolumeSummary(string? Source, string Target, bool IsBindMount, bool LooksLikePublishBinding);
