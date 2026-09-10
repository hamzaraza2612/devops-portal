namespace DevOpsPortal.Infrastructure.Secrets;

/// <summary>Must be provided via configuration/env (SECRET_ENCRYPTION_KEY).
/// Minimum 32 bytes — validated at startup exactly like Jwt:SigningKey. Never
/// has a real value in appsettings.json (checked in as an empty string, like
/// every other secret-shaped setting in this codebase).</summary>
public class SecretEncryptionSettings
{
    public const string SectionName = "Secrets";

    public string EncryptionKey { get; set; } = string.Empty;
}
