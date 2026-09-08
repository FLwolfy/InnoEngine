using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Inno.Build.Toolchains;

/// <summary>
/// Defines the deterministic filter and naming policy used to collect one native product.
/// </summary>
/// <param name="buildDirName">
/// The dependency-relative directory searched for native build outputs.
/// </param>
/// <param name="libraryTokens">
/// File-name tokens that identify artifacts belonging to the product.
/// </param>
/// <param name="extensions">
/// The accepted native artifact extensions.
/// </param>
/// <param name="requiredPathTokens">
/// Optional normalized path tokens used to reject unrelated intermediate files.
/// </param>
/// <param name="normalizeOutputName">
/// The deterministic output naming policy.
/// </param>
/// <returns>
/// The value produced by this implementation of the contract.
/// </returns>
public sealed record BuildArtifactOptions(
    string buildDirName,
    IReadOnlyCollection<string> libraryTokens,
    IReadOnlyCollection<string> extensions,
    IReadOnlyCollection<string>? requiredPathTokens,
    Func<string, string, string> normalizeOutputName
);

/// <summary>
/// Copies filtered native outputs into the engine's rebuildable dependency store.
/// </summary>
public static class BuildArtifactCopier
{
    private const string DSYM_TOKEN = ".dSYM";

    /// <summary>
    /// Copies all artifacts accepted by one product policy into its output directory.
    /// </summary>
    /// <param name="buildRoot">
    /// The native dependency root containing the configured build directory.
    /// </param>
    /// <param name="outputDir">
    /// The destination directory receiving normalized artifacts.
    /// </param>
    /// <param name="config">
    /// The normalized native build configuration.
    /// </param>
    /// <param name="options">
    /// The immutable filtering and naming policy.
    /// </param>
    public static void CopyArtifacts(string buildRoot, string outputDir, string config, BuildArtifactOptions options)
    {
        var buildDir = Path.Combine(buildRoot, options.buildDirName);
        if (!Directory.Exists(buildDir))
        {
            Console.WriteLine($"No {options.buildDirName} directory found. Nothing to copy.");
            return;
        }

        var candidates = Directory.EnumerateFiles(buildDir, "*", SearchOption.AllDirectories)
            .Where(path =>
            {
                if (path.Contains(DSYM_TOKEN, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                var ext = Path.GetExtension(path);
                if (!options.extensions.Contains(ext, StringComparer.OrdinalIgnoreCase))
                {
                    return false;
                }

                var fileName = Path.GetFileName(path);
                if (!ToolchainEnvironment.ContainsAny(fileName, options.libraryTokens.ToArray()))
                {
                    return false;
                }

                if (options.requiredPathTokens == null || options.requiredPathTokens.Count == 0)
                {
                    return true;
                }

                var normalized = path.Replace('\\', '/');
                return ToolchainEnvironment.ContainsAny(normalized, options.requiredPathTokens.ToArray());
            })
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (candidates.Count == 0)
        {
            Console.WriteLine($"No matching artifacts found under {options.buildDirName}.");
            return;
        }

        Directory.CreateDirectory(outputDir);
        foreach (var src in candidates)
        {
            var destName = options.normalizeOutputName(Path.GetFileName(src), config);
            var dest = Path.Combine(outputDir, destName);
            File.Copy(src, dest, overwrite: true);
        }
    }
}
