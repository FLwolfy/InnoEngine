using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Inno.Build;

/// <summary>
/// Resolves and validates installed Player Support Packs without loading authoring services.
/// </summary>
public sealed class PlayerSupportPackCatalog
{
    private static readonly HashSet<string> S_FORBIDDEN_EXTENSIONS = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cs", ".csproj", ".dbg", ".map", ".pdb", ".rsp", ".sln", ".xml"
    };

    private static readonly string[] S_FORBIDDEN_NAME_TOKENS =
    [
        "Microsoft.CodeAnalysis",
        "Roslyn",
        "Inno.Editor",
        "Inno.Build",
        "Inno.Scripting.Compiler",
        "Inno.Assets.Pipeline",
        "Inno.Plugins.Authoring",
        "shaderc",
        "texturec"
    ];

    private readonly string m_root;

    /// <summary>
    /// Creates a catalog rooted at the directory containing target-specific Player Support Packs.
    /// </summary>
    /// <param name="root">
    /// The directory whose child names are stable <see cref="BuildTargetId"/> values.
    /// </param>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="root"/> is empty.
    /// </exception>
    public PlayerSupportPackCatalog(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        m_root = Path.GetFullPath(root);
    }

    /// <summary>
    /// Resolves one installed Support Pack after validating its deployment-only closure.
    /// </summary>
    /// <param name="target">
    /// The platform and architecture identity of the required Support Pack.
    /// </param>
    /// <returns>
    /// The normalized directory containing the verified Support Pack.
    /// </returns>
    /// <exception cref="DirectoryNotFoundException">
    /// Thrown when no Support Pack is installed for <paramref name="target"/>.
    /// </exception>
    /// <exception cref="InvalidDataException">
    /// Thrown when the pack is empty, contains build-time payload, or lacks its Player executable or native runtimes.
    /// </exception>
    public string Resolve(BuildTargetId target)
    {
        string directory = Path.Combine(m_root, target.value);
        if (!Directory.Exists(directory))
        {
            throw new DirectoryNotFoundException(
                $"Player Support Pack '{target}' is not installed at '{directory}'. " +
                "Install or generate the target Support Pack before exporting.");
        }
        string[] files = Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories).ToArray();
        if (files.Length == 0)
            throw new InvalidDataException($"Player Support Pack '{target}' is empty.");
        foreach (string file in files)
        {
            string name = Path.GetFileName(file);
            if (S_FORBIDDEN_EXTENSIONS.Contains(Path.GetExtension(name))
                || S_FORBIDDEN_NAME_TOKENS.Any(token => name.Contains(token, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidDataException(
                    $"Player Support Pack '{target}' contains forbidden build-time file '{name}'.");
            }
        }
        string executable = target == BuildTargetId.macOSArm64
            ? Path.Combine(directory, "Inno.Player")
            : Path.Combine(directory, "Inno.Player.exe");
        if (!File.Exists(executable))
            throw new InvalidDataException($"Player Support Pack '{target}' has no Player executable.");
        string[] requiredNativeFiles = target == BuildTargetId.macOSArm64
            ? ["libbgfx-shared-lib-release.dylib", "SDL3-release.dylib", "libminiaudio-release.dylib"]
            : ["bgfx-shared-lib-release.dll", "SDL3-release.dll", "miniaudio-release.dll"];
        foreach (string requiredNativeFile in requiredNativeFiles)
        {
            if (!files.Any(file => string.Equals(
                    Path.GetFileName(file),
                    requiredNativeFile,
                    StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidDataException(
                    $"Player Support Pack '{target}' is missing native runtime '{requiredNativeFile}'.");
            }
        }
        return directory;
    }
}
