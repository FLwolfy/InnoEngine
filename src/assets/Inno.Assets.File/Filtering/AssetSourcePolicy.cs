using System;
using System.Collections.Generic;
using System.IO;

namespace Inno.Assets.File;

/// <summary>Defines which physical entries are excluded from an asset source tree.</summary>
public sealed class AssetSourcePolicy
{
    private static readonly string[] S_DEFAULT_FILE_NAMES =
    [
        ".DS_Store",
        ".directory",
        "Desktop.ini",
        "ehthumbs.db",
        "Icon\r",
        "Thumbs.db"
    ];

    private static readonly string[] S_DEFAULT_DIRECTORY_NAMES =
    [
        ".git",
        ".hg",
        ".idea",
        ".svn",
        ".vs",
        ".vscode",
        "__MACOSX",
        "bin",
        "obj"
    ];

    private static readonly string[] S_DEFAULT_PREFIXES = [".#", "._", "~$"];
    private static readonly string[] S_DEFAULT_SUFFIXES =
        [".bak", ".deps.json", ".orig", ".pdb", ".swo", ".swp", ".temp", ".tmp", "~"];

    private readonly HashSet<string> m_fileNames;
    private readonly HashSet<string> m_directoryNames;
    private readonly string[] m_prefixes;
    private readonly string[] m_suffixes;

    /// <summary>Creates a source policy using the engine's default noise filters.</summary>
    public AssetSourcePolicy()
        : this(null, null, null, null)
    {
    }

    /// <summary>Creates a source policy with additional ignored names and affixes.</summary>
    public AssetSourcePolicy(
        IEnumerable<string>? ignoredFileNames,
        IEnumerable<string>? ignoredDirectoryNames,
        IEnumerable<string>? ignoredPrefixes,
        IEnumerable<string>? ignoredSuffixes)
    {
        m_fileNames = new HashSet<string>(S_DEFAULT_FILE_NAMES, StringComparer.OrdinalIgnoreCase);
        m_directoryNames = new HashSet<string>(S_DEFAULT_DIRECTORY_NAMES, StringComparer.OrdinalIgnoreCase);
        AddNonEmpty(m_fileNames, ignoredFileNames);
        AddNonEmpty(m_directoryNames, ignoredDirectoryNames);
        m_prefixes = Combine(S_DEFAULT_PREFIXES, ignoredPrefixes);
        m_suffixes = Combine(S_DEFAULT_SUFFIXES, ignoredSuffixes);
    }

    /// <summary>Gets the default source policy.</summary>
    public static AssetSourcePolicy defaultPolicy { get; } = new();

    /// <summary>Determines whether an entry is excluded from the source database.</summary>
    public bool IsIgnored(string relativePath, bool isDirectory)
    {
        string name = Path.GetFileName(relativePath);
        if (string.IsNullOrEmpty(name))
            return false;
        if (isDirectory && m_directoryNames.Contains(name))
            return true;
        if (!isDirectory && (m_fileNames.Contains(name) || IsGeneratedPath(name)))
            return true;
        for (int i = 0; i < m_prefixes.Length; i++)
        {
            if (name.StartsWith(m_prefixes[i], StringComparison.OrdinalIgnoreCase))
                return true;
        }
        for (int i = 0; i < m_suffixes.Length; i++)
        {
            if (name.EndsWith(m_suffixes[i], StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    /// <summary>Determines whether a path is generated asset metadata.</summary>
    public static bool IsGeneratedPath(string relativePath)
        => relativePath.EndsWith(".imeta", StringComparison.OrdinalIgnoreCase) ||
           relativePath.EndsWith(".abin", StringComparison.OrdinalIgnoreCase);

    private static void AddNonEmpty(HashSet<string> target, IEnumerable<string>? values)
    {
        if (values is null)
            return;
        foreach (string value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
                target.Add(value.Trim());
        }
    }

    private static string[] Combine(string[] defaults, IEnumerable<string>? additional)
    {
        var values = new List<string>(defaults);
        if (additional is not null)
        {
            foreach (string value in additional)
            {
                if (!string.IsNullOrWhiteSpace(value))
                    values.Add(value.Trim());
            }
        }
        return values.ToArray();
    }
}
