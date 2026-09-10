using DevOpsPortal.Application.Common;
using Xunit;

namespace DevOpsPortal.Tests.Common;

public class ApprovalTokenHelperTests
{
    [Fact]
    public void Generate_ProducesA256BitHexToken_AndAMatchingHash()
    {
        var (raw, hash) = ApprovalTokenHelper.Generate();

        Assert.Equal(64, raw.Length); // 32 bytes, hex-encoded
        Assert.Matches("^[0-9A-F]{64}$", raw);
        Assert.Equal(hash, ApprovalTokenHelper.Hash(raw));
    }

    [Fact]
    public void Generate_NeverProducesTheSameTokenTwice()
    {
        var (raw1, _) = ApprovalTokenHelper.Generate();
        var (raw2, _) = ApprovalTokenHelper.Generate();

        Assert.NotEqual(raw1, raw2);
    }

    [Fact]
    public void Hash_IsDeterministic()
    {
        var (raw, _) = ApprovalTokenHelper.Generate();

        Assert.Equal(ApprovalTokenHelper.Hash(raw), ApprovalTokenHelper.Hash(raw));
    }

    [Fact]
    public void Verify_WithCorrectToken_ReturnsTrue()
    {
        var (raw, hash) = ApprovalTokenHelper.Generate();
        Assert.True(ApprovalTokenHelper.Verify(raw, hash));
    }

    [Fact]
    public void Verify_WithWrongToken_ReturnsFalse()
    {
        var (_, hash) = ApprovalTokenHelper.Generate();
        var (otherRaw, _) = ApprovalTokenHelper.Generate();

        Assert.False(ApprovalTokenHelper.Verify(otherRaw, hash));
    }
}
