using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Inno.Build.Toolchains.Bgfx;
using Inno.Build.Toolchains.Bgfx.Platforms;
using Inno.Build.Toolchains;

namespace Inno.Build.Toolchains.Bgfx;

internal static class BgfxToolsBuild
{
    /// <summary>
    /// Executes the configured workflow and returns its process outcome.
    /// </summary>
    /// <param name="args">
    /// The command-line arguments that configure this invocation.
    /// </param>
    /// <returns>
    /// The scalar result calculated from the supplied inputs.
    /// </returns>
    public static int Run(string[] args)
    {
        try
        {
            var options = ToolsBuildOptions.Parse(args);
            var repoRoot = ToolchainEnvironment.FindRepoRoot();
            var externDir = Path.Combine(repoRoot, ToolchainLayout.C_EXTERNAL_DIRECTORY_NAME);
            var bgfxDir = Path.Combine(externDir, BgfxBuildConstants.BGFX_DIR_NAME);
            var bxDir = Path.Combine(externDir, BgfxBuildConstants.BX_DIR_NAME);
            var bimgDir = Path.Combine(externDir, BgfxBuildConstants.BIMG_DIR_NAME);
            var builder = BgfxBuilderFactory.CreateForCurrentPlatform();
            var outputDir = Path.Combine(repoRoot, ToolchainLayout.C_OUTPUT_DIRECTORY_NAME, BgfxBuildConstants.OUTPUT_PRODUCT_DIR_NAME, builder.outputPlatform);

            Directory.CreateDirectory(externDir);
            Directory.CreateDirectory(outputDir);

            BgfxBuildUtils.ValidateSubmodules(bgfxDir, bxDir, bimgDir);

            EnsureBgfxBuilt(outputDir, options.Config);
            builder.BuildTools(bgfxDir, options.Config);
            CopyTools(bgfxDir, outputDir, options.Config);

            Console.WriteLine($"bgfx tools build complete. Output: {outputDir}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static void CopyTools(string bgfxDir, string outputDir, string config)
    {
        var buildDir = Path.Combine(bgfxDir, BgfxBuildConstants.BUILD_DIR_NAME);
        if (!Directory.Exists(buildDir))
        {
            return;
        }

        var toolDir = Path.Combine(outputDir, "tools");
        Directory.CreateDirectory(toolDir);

        var toolNames = new[]
        {
            "shaderc",
            "geometryc",
            "geometryv",
            "texturec",
            "texturev",
        };

        var candidates = Directory.EnumerateFiles(buildDir, "*", SearchOption.AllDirectories)
            .Where(path =>
            {
                var fileName = Path.GetFileName(path);
                if (!toolNames.Any(name => fileName.StartsWith(name, StringComparison.OrdinalIgnoreCase)
                                           || fileName.StartsWith($"{name}.exe", StringComparison.OrdinalIgnoreCase)))
                {
                    return false;
                }

                var normalized = path.Replace('\\', '/');
                return normalized.Contains("/bin/");
            })
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var src in candidates)
        {
            var destName = NormalizeToolName(Path.GetFileName(src), config);
            var dest = Path.Combine(toolDir, destName);
            File.Copy(src, dest, overwrite: true);
        }
    }

    private static string NormalizeToolName(string fileName, string config)
    {
        var ext = Path.GetExtension(fileName);
        var baseName = Path.GetFileNameWithoutExtension(fileName);
        var normalized = TrimConfigSuffix(baseName);
        return $"{normalized}-{config}{ext}";
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

    private static void EnsureBgfxBuilt(string outputDir, string config)
    {
        if (!Directory.Exists(outputDir))
        {
            throw new InvalidOperationException("bgfx outputs not found. Run Inno.Build.Toolchains.Bgfx first.");
        }

        var candidates = Directory.EnumerateFiles(outputDir, "*", SearchOption.TopDirectoryOnly)
            .Where(path =>
            {
                var ext = Path.GetExtension(path);
                if (ext is not (".dylib" or ".dll" or ".so" or ".a" or ".lib"))
                {
                    return false;
                }

                var name = Path.GetFileNameWithoutExtension(path);
                return name.EndsWith($"-{config}", StringComparison.OrdinalIgnoreCase);
            })
            .ToList();

        if (candidates.Count == 0)
        {
            throw new InvalidOperationException("bgfx outputs not found. Run Inno.Build.Toolchains.Bgfx first.");
        }
    }

}

internal sealed record ToolsBuildOptions(string Config)
{
    /// <summary>
    /// Parses validated input into the strongly typed state required by the caller.
    /// </summary>
    /// <param name="args">
    /// The command-line arguments that configure this invocation.
    /// </param>
    /// <returns>
    /// The validated tools build options that represents the completed operation.
    /// </returns>
    public static ToolsBuildOptions Parse(string[] args)
    {
        var config = ToolchainEnvironment.DefaultConfig();

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            switch (arg)
            {
                case "--config":
                    config = GetNext(args, ref i).ToLowerInvariant();
                    break;
                default:
                    throw new ArgumentException($"Unknown argument: {arg}");
            }
        }

        if (config is not (ToolchainLayout.C_DEBUG_CONFIGURATION or ToolchainLayout.C_RELEASE_CONFIGURATION))
        {
            throw new ArgumentException("--config must be 'debug' or 'release'.");
        }

        return new ToolsBuildOptions(config);
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
