namespace DevOpsPortal.Infrastructure.Security;

public class JwtSettings
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = "devops-portal";
    public string Audience { get; set; } = "devops-portal";

    /// <summary>Must be provided via configuration/env (JWT_SIGNING_KEY). Minimum 32 bytes.</summary>
    public string SigningKey { get; set; } = string.Empty;

    public int ExpiryMinutes { get; set; } = 480;
}
