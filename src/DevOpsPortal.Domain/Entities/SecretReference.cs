using DevOpsPortal.Domain.Enums;

namespace DevOpsPortal.Domain.Entities;

/// <summary>
/// Metadata about a secret — never the value. The value lives behind
/// ISecretProvider, addressed by ProviderKey+StoreKey; this row only knows
/// where to ask, not what the answer is. See master requirements §1: "The
/// application configuration should store only references/metadata, not
/// plaintext secrets."
/// </summary>
public class SecretReference
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Human-chosen identifier, unique within its (Scope, ApplicationId,
    /// EnvironmentDefinitionId) tuple — e.g. "db-password". Enforced in
    /// SecretReferenceService rather than a DB unique index, since SQL NULL
    /// comparison semantics don't give the right uniqueness behavior for the
    /// nullable Application/Environment columns.</summary>
    public string Name { get; set; } = string.Empty;

    public SecretCategory Category { get; set; }

    /// <summary>See SecretScope — the structural mechanism behind environment
    /// isolation. ApplicationId/EnvironmentDefinitionId below must be
    /// consistent with this value (validated in SecretReferenceService).</summary>
    public SecretScope Scope { get; set; }

    public Guid? ApplicationId { get; set; }
    public ManagedApplication? Application { get; set; }

    public Guid? EnvironmentDefinitionId { get; set; }
    public EnvironmentDefinition? EnvironmentDefinition { get; set; }

    public string? Description { get; set; }

    /// <summary>Which ISecretProvider implementation holds the value — lets
    /// multiple backends (server-side encryption today; Vault/cloud/K8s later)
    /// coexist without any schema change.</summary>
    public string ProviderKey { get; set; } = string.Empty;

    /// <summary>Opaque key/path within that provider's store. Deliberately
    /// never exposed via the API — it names where the ciphertext lives, not
    /// the ciphertext itself, but there's no reason to hand it out either.</summary>
    public string StoreKey { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    public Guid CreatedByUserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAt { get; set; }
}
