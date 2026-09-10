namespace DevOpsPortal.Domain.Enums;

/// <summary>
/// How an application's environments are deployed. See master requirements §3.
/// Existing applications may stay on LegacyFilesystem indefinitely; new
/// applications should prefer ContainerImage. Both are first-class.
/// </summary>
public enum DeploymentMode
{
    LegacyFilesystem = 0,
    ContainerImage = 1,
}
