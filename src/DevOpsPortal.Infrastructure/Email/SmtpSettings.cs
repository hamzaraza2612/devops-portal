namespace DevOpsPortal.Infrastructure.Email;

public class SmtpSettings
{
    public const string SectionName = "Smtp";

    /// <summary>Empty by default — SMTP is treated as "not configured" and email sending
    /// no-ops (logs + returns false) rather than failing the caller.</summary>
    public string Host { get; set; } = string.Empty;

    public int Port { get; set; } = 587;
    public bool EnableSsl { get; set; } = true;
    public string? Username { get; set; }
    public string? Password { get; set; }
    public string FromAddress { get; set; } = string.Empty;
    public string FromName { get; set; } = "DevOps Portal";
}
