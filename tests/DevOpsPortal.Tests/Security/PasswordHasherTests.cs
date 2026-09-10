using DevOpsPortal.Infrastructure.Security;
using Xunit;

namespace DevOpsPortal.Tests.Security;

public class PasswordHasherTests
{
    [Fact]
    public void Hash_ThenVerify_WithCorrectPassword_Succeeds()
    {
        var hasher = new PasswordHasher();
        var hash = hasher.Hash("Sup3rSecret!");

        Assert.True(hasher.Verify(hash, "Sup3rSecret!"));
    }

    [Fact]
    public void Verify_WithWrongPassword_Fails()
    {
        var hasher = new PasswordHasher();
        var hash = hasher.Hash("Sup3rSecret!");

        Assert.False(hasher.Verify(hash, "WrongPassword"));
    }

    [Fact]
    public void Hash_DoesNotReturnPlaintext()
    {
        var hasher = new PasswordHasher();
        var hash = hasher.Hash("Sup3rSecret!");

        Assert.DoesNotContain("Sup3rSecret!", hash);
    }
}
