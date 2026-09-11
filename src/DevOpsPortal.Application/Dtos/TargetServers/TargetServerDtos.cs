using DevOpsPortal.Domain.Enums;

namespace DevOpsPortal.Application.Dtos.TargetServers;

public record TargetServerDto(
    Guid Id,
    string Name,
    string? Description,
    string? Hostname,
    int SshPort,
    string? SshUsername,
    SshAuthMethod SshAuthMethod,
    bool HasSshCredential,
    bool HasSshPassphrase,
    bool IsActive,
    DateTimeOffset CreatedAt,
    IReadOnlyList<AllowedDeploymentRootDto> AllowedDeploymentRoots);

public record CreateTargetServerRequest(
    string Name, string? Description, string? Hostname, int SshPort, string? SshUsername, SshAuthMethod SshAuthMethod);

public record UpdateTargetServerRequest(
    string Name, string? Description, string? Hostname, int SshPort, string? SshUsername, SshAuthMethod SshAuthMethod, bool IsActive);

/// <summary>Value is either the PEM-formatted private key text (SshAuthMethod.PrivateKey)
/// or the plaintext password (SshAuthMethod.Password), depending on the target server's
/// current SshAuthMethod — stored via the same encrypted secret store every other
/// credential in the portal uses (ISecretProvider) and never returned by any API.</summary>
public record SetSshCredentialRequest(string Value);

/// <summary>Only meaningful when SshAuthMethod is PrivateKey and the key itself is
/// passphrase-protected. A null/empty Value clears a previously-set passphrase.</summary>
public record SetSshPassphraseRequest(string? Value);

public record TargetServerConnectionTestResultDto(
    bool SshConnected,
    string? AuthenticatedUser,
    string? OsInfo,
    bool DockerAvailable,
    string? DockerVersion,
    bool ComposeAvailable,
    string? ComposeVersion,
    string? ErrorMessage,
    DateTimeOffset TestedAt);

public record AllowedDeploymentRootDto(Guid Id, Guid TargetServerId, string RootPath, string? Description, bool IsActive);

public record CreateAllowedDeploymentRootRequest(string RootPath, string? Description);

public record UpdateAllowedDeploymentRootRequest(string RootPath, string? Description, bool IsActive);
