using System;
using System.IO;

namespace Inno.Assets.IO;

/// <summary>
/// Path helpers for asset-relative paths.
/// </summary>
public static class AssetPath
{
    /// <summary>
    /// Normalizes a relative path to slash-separated canonical format.
    /// </summary>
    /// <param name="relativePath">Input relative path.</param>
    /// <returns>Normalized relative path.</returns>
    public static string Normalize(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
            return string.Empty;

        string path = relativePath.Replace('\\', '/').Trim();
        while (path.StartsWith("./", StringComparison.Ordinal))
            path = path.Substring(2);
        while (path.StartsWith("/", StringComparison.Ordinal))
            path = path.Substring(1);
        while (path.EndsWith("/", StringComparison.Ordinal))
            path = path.Substring(0, path.Length - 1);

        if (path == ".")
            return string.Empty;

        return path;
    }

    /// <summary>
    /// Combines two relative paths and normalizes the output.
    /// </summary>
    /// <param name="a">Base relative path.</param>
    /// <param name="b">Child relative path.</param>
    /// <returns>Combined normalized path.</returns>
    public static string Combine(string a, string b)
        => Normalize(Path.Combine(Normalize(a), Normalize(b)));
}
