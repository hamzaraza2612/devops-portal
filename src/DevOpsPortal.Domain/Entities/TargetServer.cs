using DevOpsPortal.Domain.Enums;

namespace DevOpsPortal.Domain.Entities;

/// <summary>
/// A host that runs application containers, reachable over SSH from the
/// portal VM (which — per the platform's own target architecture — is a
/// separate machine from every TargetServer). Connection details here are
/// what <see cref="IRemoteExecutionProvider"/>'s real (SSH-based)
/// implementation uses to actually reach it; <see cref="SshCredentialStoreKey"/>
/// is an opaque reference into the same encrypted secret store
/// <c>SecretReference</c> already uses (see <c>ISecretProvider</c>) — the
/// actual private key/password bytes are never stored here, never in Git,
/// and never returned by any API response.
/// </summary>
public class TargetServer
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    /// <summary>Hostname or IP address the portal connects to over SSH.</summary>
    public string? Hostname { get; set; }

    public int SshPort { get; set; } = 22;
    public string? SshUsername { get; set; }
    public SshAuthMethod SshAuthMethod { get; set; } = SshAuthMethod.PrivateKey;

    /// <summary>Opaque key into the encrypted secret store holding either the SSH
    /// private key (PEM text) or the password, depending on SshAuthMethod — never
    /// the value itself. Null means SSH isn't configured yet for this server (every
    /// connection attempt then honestly reports "not configured", same principle
    /// NotConfiguredRemoteExecutionProvider already established).</summary>
    public string? SshCredentialStoreKey { get; set; }

    /// <summary>Only meaningful when SshAuthMethod is PrivateKey and the key itself is
    /// passphrase-protected. Stored the same way as the key/password (encrypted,
    /// referenced by an opaque store key), never in plaintext here.</summary>
    public string? SshPassphraseStoreKey { get; set; }

    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public ICollection<AllowedDeploymentRoot> AllowedDeploymentRoots { get; set; } = new List<AllowedDeploymentRoot>();
}
