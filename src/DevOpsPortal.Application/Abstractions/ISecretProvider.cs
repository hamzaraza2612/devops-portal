namespace DevOpsPortal.Application.Abstractions;

/// <summary>Wraps a lookup that can legitimately fail (store unreachable, key
/// not found, decryption failure) without that being an application error —
/// same idiom as GitProviderResult/BuildStatusResult. Value is the plaintext
/// secret: callers must never log it, put it in an exception message, or
/// return it from an API endpoint.</summary>
public record SecretValueResult(bool Success, string? Value, string? ErrorMessage)
{
    public static SecretValueResult Ok(string value) => new(true, value, null);
    public static SecretValueResult Fail(string message) => new(false, null, message);
}

/// <summary>
/// A secret-storage backend. SecretReference (Domain) rows carry only a
/// ProviderKey naming which implementation holds the value and a StoreKey
/// naming where — this interface is the only path from either to an actual
/// plaintext value. The first implementation (EncryptedSecretProvider,
/// Infrastructure) is a securely protected server-side store; the interface
/// is deliberately provider-agnostic so a future HashiCorp Vault, cloud
/// secret manager, or Kubernetes Secrets implementation is a new class + DI
/// registration, never a change to SecretReferenceService or any caller.
/// </summary>
public interface ISecretProvider
{
    /// <summary>Identifies this backend — stored on SecretReference.ProviderKey
    /// so a reference always knows which provider to ask.</summary>
    string ProviderKey { get; }

    /// <summary>Writes plaintextValue. Pass null for existingStoreKey to create
    /// a new secret (a fresh key is generated and returned); pass an existing
    /// key to rotate that secret's value in place (same key, new value) —
    /// callers never need to know the key format. Never logs or otherwise
    /// surfaces plaintextValue.</summary>
    Task<string> StoreAsync(string? existingStoreKey, string plaintextValue, CancellationToken cancellationToken = default);

    Task<SecretValueResult> RetrieveAsync(string storeKey, CancellationToken cancellationToken = default);

    /// <summary>Idempotent — deleting an already-absent key is not an error.</summary>
    Task DeleteAsync(string storeKey, CancellationToken cancellationToken = default);
}
