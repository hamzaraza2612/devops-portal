using System.Security.Cryptography;

namespace DevOpsPortal.Application.Common;

/// <summary>Used by DataSeeder's bootstrap admin user when the caller doesn't
/// supply a password — hands it back exactly once (never persisted in plaintext).</summary>
public static class RandomPasswordGenerator
{
    private const string Chars = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789!@#$%";

    public static string Generate()
    {
        var bytes = RandomNumberGenerator.GetBytes(20);
        return new string(bytes.Select(b => Chars[b % Chars.Length]).ToArray());
    }
}
