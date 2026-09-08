using System;
using System.IO;

using Inno.Assets;

namespace Inno.Assets.Pipeline;

/// <summary>
/// Defines authoring sample directories and their installed Plugin behavior.
/// </summary>
public static class AssetSample
{
    /// <summary>
    /// Gets the logical File Browser type used for a sample directory.
    /// </summary>
    public const string fileType = ".isample";

    /// <summary>
    /// Determines whether the final path segment uses the sample-directory naming convention.
    /// </summary>
    /// <param name="path">
    /// The isolated source path to inspect.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the final segment starts with <c>~</c>, regardless of whether
    /// the path belongs to the writable Project source or an installed Plugin source.
    /// </returns>
    public static bool HasSampleDirectoryName(AssetPath path)
    {
        if (!path.isValid || string.IsNullOrEmpty(path.localPath))
            return false;
        return Path.GetFileName(path.localPath).StartsWith('~');
    }

    /// <summary>
    /// Determines whether the final segment identifies a sample directory.
    /// </summary>
    /// <param name="path">
    /// The isolated source path to inspect.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the path belongs to an installed Plugin source and its final
    /// segment starts with <c>~</c>. A Project directory with the same name remains normal writable
    /// authoring content.
    /// </returns>
    public static bool IsRoot(AssetPath path)
    {
        return path.source != AssetSourceId.project && HasSampleDirectoryName(path);
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
    /// <see langword="true"/> when the path belongs to an installed Plugin source and any directory
    /// segment starts with <c>~</c>. Project content is never hidden from authoring import by this method.
    /// </returns>
    public static bool Contains(AssetPath path, bool isDirectory)
    {
        return path.source != AssetSourceId.project && ContainsTildeDirectory(path, isDirectory);
    }

    /// <summary>
    /// Gets the writable root-directory name produced when a sample is imported.
    /// </summary>
    /// <param name="path">
    /// A sample directory path whose final segment starts with <c>~</c>.
    /// </param>
    /// <returns>
    /// The unchanged final directory name, including every leading <c>~</c> character.
    /// </returns>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="path"/> does not identify a sample directory or has no
    /// usable name after its prefix.
    /// </exception>
    public static string GetImportName(AssetPath path)
    {
        if (!IsRoot(path))
            throw new ArgumentException("The path must identify an installed Plugin sample directory.", nameof(path));
        string name = Path.GetFileName(path.localPath);
        if (string.IsNullOrWhiteSpace(name.TrimStart('~')))
            throw new ArgumentException("A sample directory requires a name after its '~' prefix.", nameof(path));
        return name;
    }

    /// <summary>
    /// Determines whether a path belongs to an authoring-only sample subtree that must not enter a
    /// Player runtime closure.
    /// </summary>
    /// <param name="path">
    /// The isolated source path to inspect.
    /// </param>
    /// <param name="isDirectory">
    /// Whether the path itself represents a directory.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when any directory segment starts with <c>~</c>, including in the
    /// writable Project source.
    /// </returns>
    public static bool IsRuntimeExcluded(AssetPath path, bool isDirectory)
        => ContainsTildeDirectory(path, isDirectory);

    private static bool ContainsTildeDirectory(AssetPath path, bool isDirectory)
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
}
