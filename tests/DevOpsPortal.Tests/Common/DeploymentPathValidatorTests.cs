using DevOpsPortal.Application.Common;
using Xunit;

namespace DevOpsPortal.Tests.Common;

public class DeploymentPathValidatorTests
{
    [Theory]
    [InlineData("/mnt/data/apps/dmsapi", new[] { "/mnt/data/apps" }, true)]
    [InlineData("/mnt/data/apps", new[] { "/mnt/data/apps" }, true)] // exact match to the root itself
    [InlineData("/mnt/data/apps2/dmsapi", new[] { "/mnt/data/apps" }, false)] // sibling-prefix collision must NOT match
    [InlineData("/mnt/data/other/dmsapi", new[] { "/mnt/data/apps", "/mnt/data/other" }, true)]
    [InlineData("/mnt/data/apps/../../etc/passwd", new[] { "/mnt/data/apps" }, false)] // traversal rejected outright
    [InlineData("relative/path", new[] { "/mnt/data/apps" }, false)] // not absolute
    [InlineData("", new[] { "/mnt/data/apps" }, false)]
    public void IsUnderAllowedRoot_ReturnsExpected(string candidate, string[] allowedRoots, bool expected)
    {
        Assert.Equal(expected, DeploymentPathValidator.IsUnderAllowedRoot(candidate, allowedRoots));
    }

    [Theory]
    [InlineData("/mnt/data//apps/", "/mnt/data/apps")] // collapses repeated/trailing slashes
    [InlineData("/mnt/data/apps", "/mnt/data/apps")]
    public void TryNormalize_CollapsesSlashes(string input, string expected)
    {
        Assert.True(DeploymentPathValidator.TryNormalize(input, out var normalized));
        Assert.Equal(expected, normalized);
    }

    [Theory]
    [InlineData("/mnt/data/../apps")]
    [InlineData("relative")]
    [InlineData("")]
    [InlineData(null)]
    public void TryNormalize_RejectsUnsafePaths(string? input)
    {
        Assert.False(DeploymentPathValidator.TryNormalize(input, out _));
    }

    [Theory]
    [InlineData("publish", true)]
    [InlineData("Backups", true)]
    [InlineData("nested/sub", true)]
    [InlineData("../escape", false)]
    [InlineData("/absolute", false)]
    [InlineData("", false)]
    public void IsSafeRelativePath_ReturnsExpected(string path, bool expected)
    {
        Assert.Equal(expected, DeploymentPathValidator.IsSafeRelativePath(path));
    }
}
