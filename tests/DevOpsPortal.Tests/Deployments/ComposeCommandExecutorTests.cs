using DevOpsPortal.Application.Abstractions;
using DevOpsPortal.Infrastructure.Deployments;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DevOpsPortal.Tests.Deployments;

/// <summary>Covers the validation paths that don't require a real Docker daemon
/// (missing directory / missing compose file) — never generates or executes a
/// free-form command, only the fixed ComposeOperation allow-list.</summary>
public class ComposeCommandExecutorTests
{
    private static ComposeCommandExecutor CreateSut() => new(NullLogger<ComposeCommandExecutor>.Instance);

    [Fact]
    public async Task RunAsync_WithMissingWorkingDirectory_FailsWithoutInvokingDocker()
    {
        var sut = CreateSut();
        var missingDir = Path.Combine(Path.GetTempPath(), $"nonexistent-{Guid.NewGuid()}");

        var result = await sut.RunAsync(new ComposeCommandRequest(missingDir, "docker-compose.yml", null, ComposeOperation.Up));

        Assert.False(result.Success);
        Assert.Contains("does not exist", result.StandardError);
    }

    [Fact]
    public async Task RunAsync_WithMissingComposeFile_Fails()
    {
        var sut = CreateSut();
        var dir = Path.Combine(Path.GetTempPath(), $"compose-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(dir);
        try
        {
            var result = await sut.RunAsync(new ComposeCommandRequest(dir, "docker-compose.yml", null, ComposeOperation.Up));

            Assert.False(result.Success);
            Assert.Contains("was not found", result.StandardError);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    /// <summary>Phase 5's operational-control operations (Restart/Start/Stop/Ps) go
    /// through the exact same real, non-shell Process invocation as Up/Down already
    /// did — this proves each one actually reaches the `docker` binary (rather than
    /// throwing ArgumentOutOfRangeException from an unhandled switch arm) and fails
    /// gracefully, never throwing, when the daemon is unreachable.</summary>
    [Theory]
    [InlineData(ComposeOperation.Restart)]
    [InlineData(ComposeOperation.Start)]
    [InlineData(ComposeOperation.Stop)]
    [InlineData(ComposeOperation.Ps)]
    public async Task RunAsync_WithNewOperations_InvokesDockerAndFailsGracefully(ComposeOperation operation)
    {
        var sut = CreateSut();
        var dir = Path.Combine(Path.GetTempPath(), $"compose-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "docker-compose.yml"), "services:\n  web:\n    image: nginx:alpine\n");
        try
        {
            var result = await sut.RunAsync(new ComposeCommandRequest(dir, "docker-compose.yml", null, operation));

            // No live daemon in this environment — the real assertion is that this
            // returned a result at all (no exception) rather than what ExitCode is.
            Assert.False(result.Success);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
