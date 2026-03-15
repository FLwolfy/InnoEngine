using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Inno.Build.Global;

public sealed record BuildArtifactOptions(
    string buildDirName,
    IReadOnlyCollection<string> libraryTokens,
    IReadOnlyCollection<string> extensions,
    IReadOnlyCollection<string>? requiredPathTokens,
    Func<string, string, string> normalizeOutputName
);

public static class BuildArtifactCopier
{
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
                var ext = Path.GetExtension(path);
                if (!options.extensions.Contains(ext, StringComparer.OrdinalIgnoreCase))
                {
                    return false;
                }

                var fileName = Path.GetFileName(path);
                if (!GlobalBuildUtils.ContainsAny(fileName, options.libraryTokens.ToArray()))
                {
                    return false;
                }

                if (options.requiredPathTokens == null || options.requiredPathTokens.Count == 0)
                {
                    return true;
                }

                var normalized = path.Replace('\\', '/');
                return GlobalBuildUtils.ContainsAny(normalized, options.requiredPathTokens.ToArray());
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
