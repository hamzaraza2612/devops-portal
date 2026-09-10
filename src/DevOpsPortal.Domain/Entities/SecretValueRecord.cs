namespace DevOpsPortal.Domain.Entities;

/// <summary>
/// Ciphertext-only storage for EncryptedSecretProvider (Infrastructure).
/// Deliberately NOT exposed on IAppDbContext — only the concrete AppDbContext
/// carries this DbSet, so no Application-layer service (SecretReferenceService
/// included) has any code path to this table; only EncryptedSecretProvider,
/// which is constructed with the concrete AppDbContext specifically for this
/// reason, can read or write it. AES-256-GCM: Nonce+Tag are per-encryption,
/// never reused.
/// </summary>
public class SecretValueRecord
{
    public string StoreKey { get; set; } = string.Empty;

    public byte[] Ciphertext { get; set; } = [];
    public byte[] Nonce { get; set; } = [];
    public byte[] Tag { get; set; } = [];

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAt { get; set; }
}
