using DevOpsPortal.Application.Common;
using Xunit;

namespace DevOpsPortal.Tests.Common;

public class LogSanitizerTests
{
    [Theory]
    [InlineData("DB_PASSWORD=hunter2", "DB_PASSWORD=***REDACTED***")]
    [InlineData("password: hunter2", "password=***REDACTED***")]
    [InlineData("Authorization: Bearer abc123token", "Bearer ***REDACTED***")]
    [InlineData("API_KEY=sk-abcdef123456", "API_KEY=***REDACTED***")]
    [InlineData("connectionString=Host=db;Password=secret", "connectionString=***REDACTED***")]
    public void Sanitize_RedactsSecretLookingValues(string input, string expectedContains)
    {
        var result = LogSanitizer.Sanitize(input);
        Assert.Contains(expectedContains, result);
    }

    [Fact]
    public void Sanitize_DoesNotRedactNonSecretText()
    {
        const string input = "Container started successfully on port 8080.";
        Assert.Equal(input, LogSanitizer.Sanitize(input));
    }

    [Fact]
    public void Sanitize_DoesNotLeakOriginalSecretValue()
    {
        var result = LogSanitizer.Sanitize("DB_PASSWORD=hunter2 and everything worked");
        Assert.DoesNotContain("hunter2", result);
    }

    [Fact]
    public void Sanitize_HandlesNullAndEmpty()
    {
        Assert.Equal(string.Empty, LogSanitizer.Sanitize(null));
        Assert.Equal(string.Empty, LogSanitizer.Sanitize(string.Empty));
    }
}
