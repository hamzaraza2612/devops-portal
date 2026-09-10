using DevOpsPortal.Domain.Enums;

namespace DevOpsPortal.Domain.Entities;

/// <summary>
/// Build-from-source configuration for an application (Mode B — future
/// container-image builds; see master requirements §3). Phase 3 stores and
/// validates this configuration only; nothing here is executed yet — no
/// build runs, no Dockerfile is invented for applications that don't have
/// one. A future Build Worker consumes this once build execution exists.
/// One per application (0 or 1).
/// </summary>
public class BuildConfiguration
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ApplicationId { get; set; }
    public ManagedApplication Application { get; set; } = null!;

    /// <summary>Path to the .csproj/.sln to build, relative to Application.SourcePath (or repo root).</summary>
    public string? ProjectOrSolutionPath { get; set; }

    /// <summary>e.g. "Release". Not executed in Phase 3 — stored for a future build engine.</summary>
    public string? PublishConfiguration { get; set; }

    /// <summary>Relative Dockerfile path, only meaningful once ContainerImage builds are executed.</summary>
    public string? DockerfilePath { get; set; }

    /// <summary>Registry host, e.g. "registry.example.com". Never hardcoded/defaulted to a real tenant's registry.</summary>
    public string? ImageRegistry { get; set; }

    /// <summary>Repository path within the registry, e.g. "group/app".</summary>
    public string? ImageRepository { get; set; }

    public ImageTagStrategy ImageTagStrategy { get; set; } = ImageTagStrategy.CommitSha;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAt { get; set; }
}
