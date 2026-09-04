using System;
using System.IO;

namespace Inno.Core.IO;

/// <summary>
/// Resolves paths while enforcing an explicit filesystem ownership boundary.
/// </summary>
public static class PathBoundary
{
    /// <summary>
    /// Resolves a relative path beneath a root and rejects traversal outside that root.
    /// </summary>
    /// <param name="root">
    /// The owning root directory.
    /// </param>
    /// <param name="relativePath">
    /// The relative path to resolve.
    /// </param>
    /// <returns>
    /// The normalized absolute contained path.
    /// </returns>
    public static string Resolve(string root, string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        ArgumentNullException.ThrowIfNull(relativePath);
        if (Path.IsPathRooted(relativePath))
            throw new ArgumentException("A contained path must be relative.", nameof(relativePath));
        string normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        string candidate = Path.GetFullPath(relativePath.Length == 0 ? "." : relativePath, normalizedRoot);
        EnsureContains(normalizedRoot, candidate);
        return candidate;
    }

    /// <summary>
    /// Validates and normalizes an absolute path beneath a root.
    /// </summary>
    /// <param name="root">
    /// The owning root directory.
    /// </param>
    /// <param name="path">
    /// The path to validate.
    /// </param>
    /// <returns>
    /// The normalized absolute contained path.
    /// </returns>
    public static string RequireContained(string root, string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        string candidate = Path.GetFullPath(path);
        EnsureContains(normalizedRoot, candidate);
        return candidate;
    }

    private static void EnsureContains(string root, string candidate)
    {
        string prefix = root + Path.DirectorySeparatorChar;
        StringComparison comparison = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (!string.Equals(candidate, root, comparison)
            && !candidate.StartsWith(prefix, comparison))
        {
            throw new IOException($"Path '{candidate}' escapes filesystem boundary '{root}'.");
        }
    }
}
