using System.Security.Cryptography;

namespace DevOpsPortal.Application.Common;

/// <summary>Shared by DataSeeder's bootstrap platform-admin and TenantService's
/// initial tenant-admin — both need a random password when the caller doesn't
/// supply one, and both hand it back exactly once (never persisted in plaintext).</summary>
public static class RandomPasswordGenerator
{
    private const string Chars = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789!@#$%";

    public static string Generate()
    {
        var bytes = RandomNumberGenerator.GetBytes(20);
        return new string(bytes.Select(b => Chars[b % Chars.Length]).ToArray());
    }
}
