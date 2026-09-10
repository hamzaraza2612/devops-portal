using System.Text.RegularExpressions;

namespace DevOpsPortal.Application.Common;

/// <summary>
/// Best-effort redaction of secret-looking key/value pairs from free-form text
/// (process stdout/stderr, exception messages) before it is persisted as a
/// DeploymentLogEntry or FailureReason. Heuristic, not a guarantee — never a
/// substitute for not putting secrets in command output in the first place.
/// </summary>
public static partial class LogSanitizer
{
    public static string Sanitize(string? message)
    {
        if (string.IsNullOrEmpty(message))
            return message ?? string.Empty;

        var sanitized = SecretKeyValuePattern().Replace(message, m => $"{m.Groups["key"].Value}=***REDACTED***");
        sanitized = BearerTokenPattern().Replace(sanitized, "Bearer ***REDACTED***");
        return sanitized;
    }

    [GeneratedRegex(
        @"(?<key>[A-Za-z0-9_\-]*(?:PASSWORD|SECRET|TOKEN|APIKEY|API_KEY|CONNECTIONSTRING|PWD|PRIVATE_KEY)[A-Za-z0-9_\-]*)\s*[:=]\s*(?<value>[^\s""']+)",
        RegexOptions.IgnoreCase)]
    private static partial Regex SecretKeyValuePattern();

    /// <summary>Catches "Authorization: Bearer &lt;token&gt;" and similar — the key name
    /// itself ("Authorization") doesn't contain a secret hint word, so this needs its
    /// own pattern rather than relying on SecretKeyValuePattern.</summary>
    [GeneratedRegex(@"\bBearer\s+[A-Za-z0-9\-_.~+/]+=*", RegexOptions.IgnoreCase)]
    private static partial Regex BearerTokenPattern();
}
