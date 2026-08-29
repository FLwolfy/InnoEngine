using System;
using System.IO;
using System.Linq;

namespace Inno.Assets.Core;

/// <summary>Identifies one isolated asset source mount.</summary>
public record struct AssetSourceId
{
    /// <summary>Gets the writable project source identifier.</summary>
    public static AssetSourceId project => new("project");

    /// <summary>Creates a globally stable source identifier.</summary>
    /// <param name="value">Lowercase project or Plugin source identity.</param>
    public AssetSourceId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        string normalized = value.Trim();
        if (normalized.Any(static character =>
                !(character is >= 'a' and <= 'z'
                  || character is >= '0' and <= '9'
                  || character is '.' or '_' or '-')))
        {
            throw new ArgumentException(
                "An asset source ID may contain lowercase ASCII letters, digits, '.', '_', and '-' only.",
                nameof(value));
        }
        this.value = normalized;
    }

    /// <summary>Gets or sets the globally stable source value.</summary>
    public string value { get; set; }

    /// <summary>Gets whether the source identity has a usable value.</summary>
    public readonly bool isValid => !string.IsNullOrWhiteSpace(value);

    /// <inheritdoc />
    public readonly override string ToString() => value ?? string.Empty;
}

/// <summary>Addresses one asset without conflating isolated source mounts.</summary>
public record struct AssetPath
{
    private const string C_SOURCE_SEPARATOR = "::";

    /// <summary>Creates an asset path.</summary>
    /// <param name="source">Owning source mount.</param>
    /// <param name="localPath">Source-local path using forward slashes.</param>
    public AssetPath(AssetSourceId source, string localPath)
    {
        if (!source.isValid)
            throw new ArgumentException("An asset source ID must be valid.", nameof(source));
        this.source = source;
        this.localPath = NormalizeLocalPath(localPath);
    }

    /// <summary>Gets or sets the owning source mount.</summary>
    public AssetSourceId source { get; set; }

    /// <summary>Gets or sets the source-local path.</summary>
    public string localPath { get; set; }

    /// <summary>Gets whether the source and local path are valid.</summary>
    public readonly bool isValid => source.isValid && localPath is not null;

    /// <summary>Creates a path in the writable project source.</summary>
    /// <param name="localPath">Path relative to the project Assets directory.</param>
    /// <returns>A project-owned asset path.</returns>
    public static AssetPath Project(string localPath) => new(AssetSourceId.project, localPath);

    /// <summary>Parses a canonical path, treating an unqualified value as project-owned.</summary>
    /// <param name="value">Canonical or project-local path.</param>
    /// <returns>The isolated asset path.</returns>
    public static AssetPath Parse(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        int separator = value.IndexOf(C_SOURCE_SEPARATOR, StringComparison.Ordinal);
        return separator < 0
            ? Project(value)
            : new AssetPath(new AssetSourceId(value[..separator]), value[(separator + C_SOURCE_SEPARATOR.Length)..]);
    }

    /// <inheritdoc />
    public readonly override string ToString()
        => source == AssetSourceId.project
            ? localPath ?? string.Empty
            : $"{source}{C_SOURCE_SEPARATOR}{localPath}";

    private static string NormalizeLocalPath(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (Path.IsPathRooted(value)
            || value.StartsWith("/", StringComparison.Ordinal)
            || value.StartsWith('\\')
            || value.Length >= 2 && char.IsAsciiLetter(value[0]) && value[1] == ':')
        {
            throw new ArgumentException("An asset path must be source-relative.", nameof(value));
        }
        string path = value.Replace('\\', '/').Trim();
        while (path.StartsWith("./", StringComparison.Ordinal))
            path = path[2..];
        path = path.Trim('/');
        if (path.Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Any(static segment => segment is "." or ".."))
        {
            throw new ArgumentException("An asset path cannot contain traversal segments.", nameof(value));
        }
        return path;
    }
}
