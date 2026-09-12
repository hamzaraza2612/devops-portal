using DevOpsPortal.Application.Abstractions;
using DevOpsPortal.Domain.Entities;
using DevOpsPortal.Domain.Enums;
using DevOpsPortal.Infrastructure.Deployments;
using DevOpsPortal.Infrastructure.Remote;
using Xunit;

namespace DevOpsPortal.Tests.Deployments;

/// <summary>
/// SshRemoteExecutionProvider is the real IRemoteExecutionProvider (master
/// requirements §5/§7) — a live SSH server isn't available in this sandbox
/// (see PROJECT_STATE.md's remote-execution test/setup procedure), so these
/// tests cover exactly the parts that don't require one: IsConfigured's
/// honest per-server gating, and the shell-escaping/argument-construction
/// logic that is the actual command-injection defense (master requirements
/// §23) — every dynamic value must survive a quoting round-trip and every
/// unsafe input must be rejected before it ever reaches a remote command
/// string.
/// </summary>
public class SshRemoteExecutionProviderTests
{
    private static TargetServer UnconfiguredServer() => new() { Name = "unconfigured" };

    private static TargetServer ConfiguredServer() => new()
    {
        Name = "web-1",
        Hostname = "10.0.0.5",
        SshUsername = "deploy",
        SshCredentialStoreKey = "store-key-123",
    };

    [Fact]
    public void IsConfigured_WithNoHostnameUsernameOrCredential_IsFalse()
    {
        var sut = new SshRemoteExecutionProvider(new FakeSecretProvider(), NullLoggerFactory.Create<SshRemoteExecutionProvider>());
        Assert.False(sut.IsConfigured(UnconfiguredServer()));
    }

    [Fact]
    public void IsConfigured_WithHostnameUsernameAndCredential_IsTrue()
    {
        var sut = new SshRemoteExecutionProvider(new FakeSecretProvider(), NullLoggerFactory.Create<SshRemoteExecutionProvider>());
        Assert.True(sut.IsConfigured(ConfiguredServer()));
    }

    [Theory]
    [InlineData("VALID_NAME", true)]
    [InlineData("_leading_underscore", true)]
    [InlineData("has space", false)]
    [InlineData("has=equals", false)]
    [InlineData("has;semicolon", false)]
    [InlineData("has$dollar", false)]
    [InlineData("", false)]
    public void IsSafeEnvVarName_RejectsAnythingThatIsNotAPlainIdentifier(string name, bool expected) =>
        Assert.Equal(expected, PosixShellEscaper.IsSafeEnvVarName(name));

    [Theory]
    [InlineData("my-app_1.2", true)]
    [InlineData("my-app", true)]
    [InlineData("../etc/passwd", false)]
    [InlineData("app; rm -rf /", false)]
    [InlineData("$(whoami)", false)]
    [InlineData("", false)]
    public void IsSafeDockerName_RejectsAnythingThatIsNotAPlausibleDockerName(string name, bool expected) =>
        Assert.Equal(expected, PosixShellEscaper.IsSafeDockerName(name));

    [Theory]
    [InlineData("plain", "'plain'")]
    [InlineData("has space", "'has space'")]
    [InlineData("it's", "'it'\\''s'")]
    [InlineData("$(rm -rf /)", "'$(rm -rf /)'")]
    [InlineData("a'; rm -rf / #", "'a'\\''; rm -rf / #'")]
    public void Quote_SingleQuoteEscapesEveryValueSafely(string input, string expected) =>
        Assert.Equal(expected, PosixShellEscaper.Quote(input));

    [Fact]
    public void Quote_RoundTripsAnArbitraryValueContainingSingleQuotesAndMetacharacters()
    {
        // The defining property of the escaping scheme: whatever comes out of
        // Quote(), when handed to a POSIX shell as a single token, must
        // reproduce the original string byte-for-byte — never execute any part
        // of it as a separate command or substitution.
        const string dangerous = "'; docker compose down -v; echo '";
        var quoted = PosixShellEscaper.Quote(dangerous);

        Assert.StartsWith("'", quoted);
        Assert.EndsWith("'", quoted);
        // No unescaped single quote may appear except as part of the '\'' escape sequence.
        Assert.DoesNotContain("''", quoted.Replace("'\\''", string.Empty));
    }

    [Theory]
    [InlineData(ComposeOperation.Up, new[] { "up", "-d" })]
    [InlineData(ComposeOperation.Down, new[] { "down" })]
    [InlineData(ComposeOperation.DownWithVolumes, new[] { "down", "-v" })]
    [InlineData(ComposeOperation.Restart, new[] { "restart" })]
    [InlineData(ComposeOperation.Start, new[] { "start" })]
    [InlineData(ComposeOperation.Stop, new[] { "stop" })]
    [InlineData(ComposeOperation.Pull, new[] { "pull" })]
    public void ComposeOperationArgs_MapsEveryAllowedOperationToItsFixedArgumentList(ComposeOperation operation, string[] expected) =>
        Assert.Equal(expected, ComposeOperationArgs.For(operation));

    [Fact]
    public async Task RunComposeAsync_WhenNotConfigured_FailsWithoutAttemptingAnything()
    {
        var sut = new SshRemoteExecutionProvider(new FakeSecretProvider(), NullLoggerFactory.Create<SshRemoteExecutionProvider>());
        var result = await sut.RunComposeAsync(
            UnconfiguredServer(), new ComposeCommandRequest("/opt/app", "docker-compose.yml", null, ComposeOperation.Up));

        Assert.False(result.Success);
        Assert.Contains("not configured", result.StandardError);
    }

    [Fact]
    public async Task InspectContainerAsync_RejectsAnUnsafeContainerNameWithoutAttemptingAnything()
    {
        var sut = new SshRemoteExecutionProvider(new FakeSecretProvider(), NullLoggerFactory.Create<SshRemoteExecutionProvider>());
        var result = await sut.InspectContainerAsync(ConfiguredServer(), "app; rm -rf /");

        Assert.False(result.Success);
        Assert.Equal("Invalid container name.", result.Error);
    }

    [Fact]
    public async Task GetContainerLogsAsync_WhenNotConfigured_FailsWithoutAttemptingAnything()
    {
        var sut = new SshRemoteExecutionProvider(new FakeSecretProvider(), NullLoggerFactory.Create<SshRemoteExecutionProvider>());
        var result = await sut.GetContainerLogsAsync(UnconfiguredServer(), "sample-web-1", 200);

        Assert.False(result.Success);
        Assert.Equal(string.Empty, result.Logs);
        Assert.Contains("not configured", result.Error);
    }

    [Theory]
    [InlineData("app; rm -rf /")]
    [InlineData("$(whoami)")]
    [InlineData("../etc/passwd")]
    public async Task GetContainerLogsAsync_RejectsAnUnsafeContainerNameWithoutAttemptingAnything(string unsafeName)
    {
        var sut = new SshRemoteExecutionProvider(new FakeSecretProvider(), NullLoggerFactory.Create<SshRemoteExecutionProvider>());
        var result = await sut.GetContainerLogsAsync(ConfiguredServer(), unsafeName, 200);

        Assert.False(result.Success);
        Assert.Equal(string.Empty, result.Logs);
        Assert.Equal("Invalid container name.", result.Error);
    }

    [Fact]
    public async Task GetContainerStatsAsync_WhenNotConfigured_FailsWithoutAttemptingAnything()
    {
        var sut = new SshRemoteExecutionProvider(new FakeSecretProvider(), NullLoggerFactory.Create<SshRemoteExecutionProvider>());
        var result = await sut.GetContainerStatsAsync(UnconfiguredServer(), "sample-web-1");

        Assert.False(result.Success);
        Assert.Contains("not configured", result.Error);
    }

    [Fact]
    public async Task GetContainerStatsAsync_RejectsAnUnsafeContainerNameWithoutAttemptingAnything()
    {
        var sut = new SshRemoteExecutionProvider(new FakeSecretProvider(), NullLoggerFactory.Create<SshRemoteExecutionProvider>());
        var result = await sut.GetContainerStatsAsync(ConfiguredServer(), "app; rm -rf /");

        Assert.False(result.Success);
        Assert.Equal("Invalid container name.", result.Error);
    }

    [Fact]
    public async Task TestConnectionAsync_WhenNotConfigured_FailsWithoutAttemptingAnythingAndReportsNoHostMetrics()
    {
        // Phase 13 "Environment Server Dashboard": the host-metric fields
        // (UptimeInfo/MemoryInfo/DiskInfo) must be just as honest as every
        // other field here — null, never a fabricated placeholder, when the
        // connection itself was never attempted.
        var sut = new SshRemoteExecutionProvider(new FakeSecretProvider(), NullLoggerFactory.Create<SshRemoteExecutionProvider>());
        var result = await sut.TestConnectionAsync(UnconfiguredServer());

        Assert.False(result.SshConnected);
        Assert.Null(result.UptimeInfo);
        Assert.Null(result.MemoryInfo);
        Assert.Null(result.DiskInfo);
        Assert.Contains("not configured", result.ErrorMessage);
    }

    [Fact]
    public async Task DiscoverContainersAsync_WhenNotConfigured_FailsWithoutAttemptingAnything()
    {
        var sut = new SshRemoteExecutionProvider(new FakeSecretProvider(), NullLoggerFactory.Create<SshRemoteExecutionProvider>());
        var result = await sut.DiscoverContainersAsync(UnconfiguredServer());

        Assert.False(result.Success);
        Assert.Equal(string.Empty, result.InspectJson);
        Assert.Contains("not configured", result.Error);
    }

    [Fact]
    public async Task RunContainerActionAsync_WhenNotConfigured_FailsWithoutAttemptingAnything()
    {
        var sut = new SshRemoteExecutionProvider(new FakeSecretProvider(), NullLoggerFactory.Create<SshRemoteExecutionProvider>());
        var result = await sut.RunContainerActionAsync(UnconfiguredServer(), "sample-web-1", RemoteContainerAction.Restart);

        Assert.False(result.Success);
        Assert.Contains("not configured", result.Error);
    }

    [Fact]
    public async Task RunContainerActionAsync_RejectsAnUnsafeContainerIdWithoutAttemptingAnything()
    {
        var sut = new SshRemoteExecutionProvider(new FakeSecretProvider(), NullLoggerFactory.Create<SshRemoteExecutionProvider>());
        var result = await sut.RunContainerActionAsync(ConfiguredServer(), "app; rm -rf /", RemoteContainerAction.Stop);

        Assert.False(result.Success);
        Assert.Equal("Invalid container identifier.", result.Error);
    }

    [Fact]
    public async Task GetHostMetricsAsync_WhenNotConfigured_FailsWithoutAttemptingAnything()
    {
        var sut = new SshRemoteExecutionProvider(new FakeSecretProvider(), NullLoggerFactory.Create<SshRemoteExecutionProvider>());
        var result = await sut.GetHostMetricsAsync(UnconfiguredServer());

        Assert.False(result.Success);
        Assert.Null(result.LoadAvgRaw);
        Assert.Null(result.MemRaw);
        Assert.Null(result.DiskRaw);
        Assert.Contains("not configured", result.Error);
    }

    [Fact]
    public async Task GetEnvironmentSnapshotAsync_WhenNotConfigured_FailsWithoutAttemptingAnything()
    {
        var sut = new SshRemoteExecutionProvider(new FakeSecretProvider(), NullLoggerFactory.Create<SshRemoteExecutionProvider>());
        var result = await sut.GetEnvironmentSnapshotAsync(UnconfiguredServer());

        Assert.False(result.SshConnected);
        Assert.False(result.DockerAvailable);
        Assert.Equal(string.Empty, result.InspectJson);
        Assert.Equal(string.Empty, result.StatsJson);
        Assert.Contains("not configured", result.ErrorMessage);
    }

    private sealed class FakeSecretProvider : ISecretProvider
    {
        public string ProviderKey => "fake";

        public Task<string> StoreAsync(string? existingStoreKey, string plaintextValue, CancellationToken cancellationToken = default) =>
            Task.FromResult(existingStoreKey ?? Guid.NewGuid().ToString());

        public Task<SecretValueResult> RetrieveAsync(string storeKey, CancellationToken cancellationToken = default) =>
            Task.FromResult(SecretValueResult.Fail("Fake provider: no real secret store in tests."));

        public Task DeleteAsync(string storeKey, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}

file static class NullLoggerFactory
{
    public static Microsoft.Extensions.Logging.ILogger<T> Create<T>() => Microsoft.Extensions.Logging.Abstractions.NullLogger<T>.Instance;
}
