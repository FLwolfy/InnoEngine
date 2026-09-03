using System;
using System.IO;

using Inno.Assets;

namespace Inno.Assets.Pipeline;

/// <summary>
/// Defines authoring-only sample directories that remain browsable but are excluded from
/// asset importing, script compilation, and Player deployment.
/// </summary>
public static class AssetSample
{
    /// <summary>
    /// Gets the logical File Browser type used for a sample directory.
    /// </summary>
    public const string fileType = ".isample";

    /// <summary>
    /// Determines whether the final segment identifies a sample directory.
    /// </summary>
    /// <param name="path">The isolated source path to inspect.</param>
    /// <returns>
    /// <see langword="true"/> when the final segment starts with <c>~</c>.
    /// </returns>
    public static bool IsRoot(AssetPath path)
    {
        if (!path.isValid || string.IsNullOrEmpty(path.localPath))
            return false;
        string name = Path.GetFileName(path.localPath);
        return name.StartsWith('~');
    }

    /// <summary>
    /// Determines whether a source path is a sample directory or is contained by one.
    /// </summary>
    /// <param name="path">
    /// The isolated source path to inspect.
    /// </param>
    /// <param name="isDirectory">
    /// Whether the path itself represents a directory.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when any directory segment starts with <c>~</c>.
    /// </returns>
    public static bool Contains(AssetPath path, bool isDirectory)
    {
        if (!path.isValid || string.IsNullOrEmpty(path.localPath))
            return false;
        string[] segments = path.localPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        int directoryCount = isDirectory ? segments.Length : segments.Length - 1;
        for (int index = 0; index < directoryCount; index++)
        {
            if (segments[index].StartsWith('~'))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Gets the writable root-directory name produced when a sample is imported.
    /// </summary>
    /// <param name="path">
    /// A sample directory path whose final segment starts with <c>~</c>.
    /// </param>
    /// <returns>
    /// The final directory name with every leading <c>~</c> removed.
    /// </returns>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="path"/> does not identify a sample directory or has no
    /// usable name after removing its prefix.
    /// </exception>
    public static string GetImportName(AssetPath path)
    {
        if (!IsRoot(path))
            throw new ArgumentException("The path must identify an authoring-only sample directory.", nameof(path));
        string name = Path.GetFileName(path.localPath).TrimStart('~');
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("A sample directory requires a name after its '~' prefix.", nameof(path));
        return name;
    }
}
