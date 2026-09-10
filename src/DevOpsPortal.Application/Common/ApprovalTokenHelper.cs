using System.Security.Cryptography;
using System.Text;

namespace DevOpsPortal.Application.Common;

/// <summary>
/// One-time approval-link tokens: 256-bit random, hex-encoded, stored only as
/// a SHA-256 hash — the raw value exists just long enough to go into a
/// notification body and is never persisted or logged (master requirements
/// §4: approval links/tokens must be secure). Used for PromotionRequest and
/// ProductionApproval's read-only, unauthenticated "preview" deep links —
/// never as a bypass credential for the actual approve/reject decision,
/// which always goes through the normal authenticated, permission-checked
/// endpoints.
/// </summary>
public static class ApprovalTokenHelper
{
    public static (string RawToken, string Hash) Generate()
    {
        var raw = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        return (raw, Hash(raw));
    }

    public static string Hash(string rawToken) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken)));

    /// <summary>Constant-time comparison — avoids leaking timing information
    /// about how much of a guessed token matched.</summary>
    public static bool Verify(string rawToken, string expectedHash)
    {
        var actualHash = Hash(rawToken);
        return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(actualHash), Encoding.UTF8.GetBytes(expectedHash));
    }
}
