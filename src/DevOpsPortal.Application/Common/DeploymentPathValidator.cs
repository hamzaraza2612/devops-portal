namespace DevOpsPortal.Application.Common;

/// <summary>
/// Pure path-containment checks backing the target-server allow-list
/// (master requirements §20: "use allow-listed deployment targets").
/// No filesystem access — this only reasons about strings.
/// </summary>
public static class DeploymentPathValidator
{
    /// <summary>True if <paramref name="candidatePath"/> is exactly one of the allowed
    /// roots, or a subdirectory of one, after normalization.</summary>
    public static bool IsUnderAllowedRoot(string candidatePath, IEnumerable<string> allowedRoots)
    {
        if (!TryNormalize(candidatePath, out var normalizedCandidate))
            return false;

        foreach (var root in allowedRoots)
        {
            if (!TryNormalize(root, out var normalizedRoot))
                continue;

            if (normalizedCandidate == normalizedRoot ||
                normalizedCandidate.StartsWith(normalizedRoot + "/", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Normalizes a Unix-style absolute path (collapses repeated slashes,
    /// trims trailing slash) and rejects anything relative or containing traversal
    /// segments ("." / ".."). Target servers are Linux hosts.</summary>
    public static bool TryNormalize(string? path, out string normalized)
    {
        normalized = string.Empty;

        if (string.IsNullOrWhiteSpace(path))
            return false;

        path = path.Trim();

        if (!path.StartsWith('/'))
            return false;

        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Any(s => s is "." or ".."))
            return false;

        normalized = "/" + string.Join('/', segments);
        return true;
    }

    /// <summary>True for a non-empty relative path with no traversal segments — for
    /// fields like ComposeFilePath/PublishSubPath that must stay inside the
    /// application's own deployment root, not escape it via "..".</summary>
    public static bool IsSafeRelativePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        path = path.Trim();
        if (path.StartsWith('/'))
            return false;

        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segments.Length > 0 && segments.All(s => s is not ("." or ".."));
    }
}
