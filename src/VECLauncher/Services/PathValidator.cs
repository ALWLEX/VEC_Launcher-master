namespace VECLauncher.Services;

/// <summary>
/// Validates file paths to prevent path traversal attacks.
/// Ensures all file operations stay within allowed directories.
/// </summary>
public static class PathValidator
{
    /// <summary>
    /// Validates that a path is within the allowed root directory.
    /// Returns true if the path is safe, false if it's a traversal attempt.
    /// </summary>
    public static bool IsPathSafe(string path, string allowedRoot)
    {
        try
        {
            var fullPath = Path.GetFullPath(path);
            var rootFull = Path.GetFullPath(allowedRoot);
            return fullPath.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Validates that a filename doesn't contain dangerous characters.
    /// </summary>
    public static bool IsFilenameSafe(string filename)
    {
        if (string.IsNullOrWhiteSpace(filename)) return false;
        if (filename.Contains("..")) return false;
        if (filename.Contains('/') || filename.Contains('\\')) return false;
        var invalid = Path.GetInvalidFileNameChars();
        return !filename.Any(c => invalid.Contains(c));
    }

    /// <summary>
    /// Sanitizes a filename by removing dangerous characters.
    /// </summary>
    public static string SanitizeFilename(string filename)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(filename.Where(c => !invalid.Contains(c)).ToArray());
        return sanitized.Replace("..", "").Trim();
    }

    /// <summary>
    /// Combines paths safely, validating the result is within root.
    /// Returns null if the resulting path would escape the root.
    /// </summary>
    public static string? CombineSafe(string root, params string[] parts)
    {
        try
        {
            var combined = Path.Combine(new[] { root }.Concat(parts).ToArray());
            var fullPath = Path.GetFullPath(combined);
            var rootFull = Path.GetFullPath(root);
            return fullPath.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase) ? fullPath : null;
        }
        catch
        {
            return null;
        }
    }
}
