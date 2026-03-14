using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Inno.Build.Bgfx.Common;

namespace Inno.Build.Bgfx;

static class Program
{
    private static readonly string[] LIBRARY_TOKENS = [
        BgfxBuildConstants.BGFX_DIR_NAME,
        BgfxBuildConstants.BX_DIR_NAME,
        BgfxBuildConstants.BIMG_DIR_NAME,
    ];
    private static readonly string[] BUILD_PATH_TOKENS = { "/bin/", "/lib/" };
    private static readonly HashSet<string> SHARED_EXTENSIONS = new(StringComparer.OrdinalIgnoreCase)
    {
        ".dll",
        ".dylib",
        ".so",
    };

    public static int Main(string[] args)
    {
        try
        {
            var options = Options.Parse(args);
            var repoRoot = BgfxBuildUtils.FindRepoRoot();
            var externDir = Path.Combine(repoRoot, BgfxBuildConstants.EXTERN_DIR_NAME);
            var bgfxDir = Path.Combine(externDir, BgfxBuildConstants.BGFX_DIR_NAME);
            var bxDir = Path.Combine(externDir, BgfxBuildConstants.BX_DIR_NAME);
            var bimgDir = Path.Combine(externDir, BgfxBuildConstants.BIMG_DIR_NAME);
            var outputDir = Path.Combine(repoRoot, BgfxBuildConstants.OUTPUT_ROOT_DIR_NAME, BgfxBuildConstants.OUTPUT_PRODUCT_DIR_NAME, options.outputPlatform);

            Directory.CreateDirectory(externDir);
            Directory.CreateDirectory(outputDir);

            BgfxBuildUtils.ValidateSubmodules(bgfxDir, bxDir, bimgDir);

            BgfxBuildUtils.Run("make", options.makeTarget, bgfxDir);

            CopyArtifacts(bgfxDir, outputDir, options.includeStatic, options.config);
            Console.WriteLine($"bgfx build complete. Output: {outputDir}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static void CopyArtifacts(string bgfxDir, string outputDir, bool includeStatic, string config)
    {
        var buildDir = Path.Combine(bgfxDir, BgfxBuildConstants.BUILD_DIR_NAME);
        if (!Directory.Exists(buildDir))
        {
            Console.WriteLine($"No {BgfxBuildConstants.BUILD_DIR_NAME} directory found. Nothing to copy.");
            return;
        }
        var exts = new HashSet<string>(SHARED_EXTENSIONS, StringComparer.OrdinalIgnoreCase);

        if (includeStatic)
        {
            exts.Add(".a");
            exts.Add(".lib");
        }

        var candidates = Directory.EnumerateFiles(buildDir, "*", SearchOption.AllDirectories)
            .Where(path =>
            {
                var ext = Path.GetExtension(path);
                if (!exts.Contains(ext))
                {
                    return false;
                }

                var fileName = Path.GetFileName(path);
                if (!ContainsAny(fileName, LIBRARY_TOKENS))
                {
                    return false;
                }

                var normalized = path.Replace('\\', '/');
                return ContainsAny(normalized, BUILD_PATH_TOKENS);
            })
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (candidates.Count == 0)
        {
            Console.WriteLine($"No matching artifacts found under {BgfxBuildConstants.BUILD_DIR_NAME}.");
            return;
        }

        Directory.CreateDirectory(outputDir);
        foreach (var src in candidates)
        {
            var destName = NormalizeOutputName(Path.GetFileName(src), config);
            var dest = Path.Combine(outputDir, destName);
            File.Copy(src, dest, overwrite: true);
        }
    }

    private static bool ContainsAny(string value, params string[] needles)
    {
        foreach (var needle in needles)
        {
            if (value.Contains(needle, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string NormalizeOutputName(string fileName, string config)
    {
        var ext = Path.GetExtension(fileName);
        var baseName = Path.GetFileNameWithoutExtension(fileName);
        var trimmed = TrimConfigSuffix(baseName);
        return $"{trimmed}-{config}{ext}";
    }

    private static string TrimConfigSuffix(string baseName)
    {
        if (baseName.EndsWith("Release", StringComparison.OrdinalIgnoreCase))
        {
            baseName = baseName[..^"Release".Length];
        }
        else if (baseName.EndsWith("Debug", StringComparison.OrdinalIgnoreCase))
        {
            baseName = baseName[..^"Debug".Length];
        }

        return baseName.TrimEnd('-', '_', '.');
    }

}

internal sealed record Options(
    string outputPlatform,
    string makeTarget,
    bool includeStatic,
    string config)
{
    public static Options Parse(string[] args)
    {
        var config = BgfxBuildUtils.DefaultConfig();
        var makeTargetOverride = "";
        var includeStatic = false;

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            switch (arg)
            {
                case "--config":
                    config = GetNext(args, ref i).ToLowerInvariant();
                    break;
                case "--make-target":
                    makeTargetOverride = GetNext(args, ref i);
                    break;
                case "--include-static":
                    includeStatic = true;
                    break;
                default:
                    throw new ArgumentException($"Unknown argument: {arg}");
            }
        }

        if (config is not (BgfxBuildConstants.DEBUG_CONFIG or BgfxBuildConstants.RELEASE_CONFIG))
        {
            throw new ArgumentException("--config must be 'debug' or 'release'.");
        }

        var defaults = BgfxBuildUtils.DetectDefaults(config);
        var makeTarget = string.IsNullOrWhiteSpace(makeTargetOverride)
            ? defaults.MakeTarget
            : makeTargetOverride;

        return new Options(defaults.OutputPlatform, makeTarget, includeStatic, config);
    }

    private static string GetNext(string[] args, ref int index)
    {
        if (index + 1 >= args.Length)
        {
            throw new ArgumentException($"Missing value for {args[index]}.");
        }

        index++;
        return args[index];
    }

}
