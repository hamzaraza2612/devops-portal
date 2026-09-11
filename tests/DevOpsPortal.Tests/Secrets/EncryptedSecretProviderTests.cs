using DevOpsPortal.Infrastructure.Persistence;
using DevOpsPortal.Infrastructure.Secrets;
using DevOpsPortal.Tests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace DevOpsPortal.Tests.Secrets;

public class EncryptedSecretProviderTests
{
    private static AppDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static EncryptedSecretProvider CreateSut(AppDbContext db, string key = "0123456789abcdef0123456789abcdef") =>
        new(db, Options.Create(new SecretEncryptionSettings { EncryptionKey = key }), NullLogger<EncryptedSecretProvider>.Instance);

    [Fact]
    public void ProviderKey_IsStable()
    {
        var sut = CreateSut(CreateDb());
        Assert.Equal("server-encrypted", sut.ProviderKey);
    }

    [Fact]
    public async Task StoreAsync_ThenRetrieveAsync_RoundTripsThePlaintextValue()
    {
        var db = CreateDb();
        var sut = CreateSut(db);

        var storeKey = await sut.StoreAsync(null, "hunter2");
        var result = await sut.RetrieveAsync(storeKey);

        Assert.True(result.Success);
        Assert.Equal("hunter2", result.Value);
    }

    [Fact]
    public async Task StoreAsync_NeverPersistsThePlaintextValueAnywhereInTheDatabase()
    {
        var db = CreateDb();
        var sut = CreateSut(db);

        var storeKey = await sut.StoreAsync(null, "hunter2-super-secret");

        var record = await db.SecretValues.AsNoTracking().SingleAsync(v => v.StoreKey == storeKey);
        var ciphertextAsLatin1 = System.Text.Encoding.Latin1.GetString(record.Ciphertext);
        Assert.DoesNotContain("hunter2", ciphertextAsLatin1);
        Assert.NotEmpty(record.Nonce);
        Assert.NotEmpty(record.Tag);
    }

    [Fact]
    public async Task StoreAsync_WithExistingStoreKey_RotatesInPlace_SameKeyNewValue()
    {
        var db = CreateDb();
        var sut = CreateSut(db);

        var storeKey = await sut.StoreAsync(null, "old-value");
        var rotatedKey = await sut.StoreAsync(storeKey, "new-value");

        Assert.Equal(storeKey, rotatedKey);
        var result = await sut.RetrieveAsync(storeKey);
        Assert.Equal("new-value", result.Value);
        Assert.Equal(1, await db.SecretValues.CountAsync());
    }

    [Fact]
    public async Task RetrieveAsync_UnknownStoreKey_ReturnsFail_NeverThrows()
    {
        var sut = CreateSut(CreateDb());

        var result = await sut.RetrieveAsync("does-not-exist");

        Assert.False(result.Success);
        Assert.Null(result.Value);
        Assert.NotNull(result.ErrorMessage);
    }

    [Fact]
    public async Task RetrieveAsync_WithWrongEncryptionKey_ReturnsFail_NeverThrows()
    {
        var db = CreateDb();
        var storeKey = await CreateSut(db, "0123456789abcdef0123456789abcdef").StoreAsync(null, "hunter2");

        var wrongKeySut = CreateSut(db, "ffffffffffffffffffffffffffffffff");
        var result = await wrongKeySut.RetrieveAsync(storeKey);

        Assert.False(result.Success);
        Assert.Null(result.Value);
    }

    [Fact]
    public async Task DeleteAsync_RemovesTheValue_AndIsIdempotent()
    {
        var db = CreateDb();
        var sut = CreateSut(db);
        var storeKey = await sut.StoreAsync(null, "hunter2");

        await sut.DeleteAsync(storeKey);
        await sut.DeleteAsync(storeKey); // idempotent — no throw

        Assert.Equal(0, await db.SecretValues.CountAsync());
        Assert.False((await sut.RetrieveAsync(storeKey)).Success);
    }

    [Fact]
    public async Task StoreAsync_EncryptingTheSameValueTwice_ProducesDifferentCiphertext_NonceNeverReused()
    {
        var db = CreateDb();
        var sut = CreateSut(db);

        var key1 = await sut.StoreAsync(null, "same-value");
        var key2 = await sut.StoreAsync(null, "same-value");

        var record1 = await db.SecretValues.AsNoTracking().SingleAsync(v => v.StoreKey == key1);
        var record2 = await db.SecretValues.AsNoTracking().SingleAsync(v => v.StoreKey == key2);
        Assert.NotEqual(Convert.ToBase64String(record1.Nonce), Convert.ToBase64String(record2.Nonce));
        Assert.NotEqual(Convert.ToBase64String(record1.Ciphertext), Convert.ToBase64String(record2.Ciphertext));
    }
}
