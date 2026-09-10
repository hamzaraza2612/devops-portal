using System.Security.Cryptography;
using System.Text;
using DevOpsPortal.Application.Abstractions;
using DevOpsPortal.Domain.Entities;
using DevOpsPortal.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DevOpsPortal.Infrastructure.Secrets;

/// <summary>
/// First ISecretProvider implementation: AES-256-GCM encryption at rest in a
/// dedicated, narrow table (SecretValueRecord). Deliberately constructed with
/// the concrete AppDbContext, not IAppDbContext — SecretValueRecord has no
/// DbSet on the Application-layer abstraction at all, so no service other
/// than this one has any code path to the ciphertext table, regardless of
/// what it's injected with. The encryption key never lives in source control
/// or appsettings.json: it is read from Secrets:EncryptionKey (env
/// SECRET_ENCRYPTION_KEY), validated at startup exactly like Jwt:SigningKey
/// (see Program.cs). This is "a securely protected server-side secret store
/// appropriate for the product deployment" per the master requirements —
/// extensible: a future HashiCorp Vault/cloud KMS/Kubernetes Secrets
/// ISecretProvider is a new class + DI registration, no change to
/// SecretReferenceService or any other caller.
/// </summary>
public class EncryptedSecretProvider(AppDbContext db, IOptions<SecretEncryptionSettings> options, ILogger<EncryptedSecretProvider> logger)
    : ISecretProvider
{
    private const int NonceSizeBytes = 12;
    private const int TagSizeBytes = 16;
    private const int KeySizeBytes = 32;

    private readonly byte[] _key = DeriveKey(options.Value.EncryptionKey);

    public string ProviderKey => "server-encrypted";

    public async Task<string> StoreAsync(string? existingStoreKey, string plaintextValue, CancellationToken cancellationToken = default)
    {
        var storeKey = existingStoreKey ?? Guid.NewGuid().ToString("N");

        var nonce = RandomNumberGenerator.GetBytes(NonceSizeBytes);
        var plaintextBytes = Encoding.UTF8.GetBytes(plaintextValue);
        var ciphertext = new byte[plaintextBytes.Length];
        var tag = new byte[TagSizeBytes];

        using (var aes = new AesGcm(_key, TagSizeBytes))
            aes.Encrypt(nonce, plaintextBytes, ciphertext, tag);

        var record = await db.SecretValues.FirstOrDefaultAsync(v => v.StoreKey == storeKey, cancellationToken);
        if (record is null)
        {
            record = new SecretValueRecord { StoreKey = storeKey };
            db.SecretValues.Add(record);
        }
        else
        {
            record.UpdatedAt = DateTimeOffset.UtcNow;
        }

        record.Ciphertext = ciphertext;
        record.Nonce = nonce;
        record.Tag = tag;
        await db.SaveChangesAsync(cancellationToken);

        return storeKey;
    }

    public async Task<SecretValueResult> RetrieveAsync(string storeKey, CancellationToken cancellationToken = default)
    {
        var record = await db.SecretValues.AsNoTracking().FirstOrDefaultAsync(v => v.StoreKey == storeKey, cancellationToken);
        if (record is null)
            return SecretValueResult.Fail("Secret value was not found in the secret store.");

        try
        {
            var plaintextBytes = new byte[record.Ciphertext.Length];
            using (var aes = new AesGcm(_key, TagSizeBytes))
                aes.Decrypt(record.Nonce, record.Ciphertext, record.Tag, plaintextBytes);

            return SecretValueResult.Ok(Encoding.UTF8.GetString(plaintextBytes));
        }
        catch (CryptographicException ex)
        {
            // Never include record.Ciphertext/Nonce/Tag or any derived text in the
            // log — only the fact that decryption failed and which key it was for.
            logger.LogError(ex, "Failed to decrypt secret value for store key {StoreKey}", storeKey);
            return SecretValueResult.Fail("Secret value could not be decrypted.");
        }
    }

    public async Task DeleteAsync(string storeKey, CancellationToken cancellationToken = default)
    {
        var record = await db.SecretValues.FirstOrDefaultAsync(v => v.StoreKey == storeKey, cancellationToken);
        if (record is null)
            return;

        db.SecretValues.Remove(record);
        await db.SaveChangesAsync(cancellationToken);
    }

    private static byte[] DeriveKey(string configuredKey)
    {
        var keyBytes = Encoding.UTF8.GetBytes(configuredKey);
        if (keyBytes.Length < KeySizeBytes)
        {
            throw new InvalidOperationException(
                "Secrets:EncryptionKey (env SECRET_ENCRYPTION_KEY) must be configured with at least 32 bytes before starting the API.");
        }

        return keyBytes[..KeySizeBytes];
    }
}
