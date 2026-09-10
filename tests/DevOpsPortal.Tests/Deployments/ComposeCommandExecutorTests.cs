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
}
