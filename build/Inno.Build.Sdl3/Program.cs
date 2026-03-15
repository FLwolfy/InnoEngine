using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Inno.Build.Global;
using Inno.Build.Sdl3.Platforms;

namespace Inno.Build.Sdl3;

static class Program
{
    private static readonly string[] LIBRARY_TOKENS = ["SDL3"];
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
            var builder = Sdl3BuilderFactory.CreateForCurrentPlatform();
            var repoRoot = GlobalBuildUtils.FindRepoRoot();
            var externDir = Path.Combine(repoRoot, GlobalBuildConstants.EXTERN_DIR_NAME);
            var sdlDir = Path.Combine(externDir, Sdl3BuildConstants.SDL_DIR_NAME);
            var outputDir = Path.Combine(repoRoot, GlobalBuildConstants.OUTPUT_ROOT_DIR_NAME, Sdl3BuildConstants.OUTPUT_PRODUCT_DIR_NAME, builder.OutputPlatform);

            Directory.CreateDirectory(externDir);
            Directory.CreateDirectory(outputDir);

            Sdl3BuildUtils.ValidateSource(sdlDir);

            builder.Build(sdlDir, options.Config);
            CopyArtifacts(sdlDir, outputDir, options.Config);

            Console.WriteLine($"SDL3 build complete. Output: {outputDir}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static void CopyArtifacts(string sdlDir, string outputDir, string config)
    {
        var buildRoot = Path.Combine(sdlDir, Sdl3BuildConstants.BUILD_DIR_NAME);
        if (!Directory.Exists(buildRoot))
        {
            Console.WriteLine($"No {Sdl3BuildConstants.BUILD_DIR_NAME} directory found. Nothing to copy.");
            return;
        }

        var candidates = Directory.EnumerateFiles(buildRoot, "*", SearchOption.AllDirectories)
            .Where(path =>
            {
                var ext = Path.GetExtension(path);
                if (!SHARED_EXTENSIONS.Contains(ext))
                {
                    return false;
                }

                var fileName = Path.GetFileName(path);
                if (!ContainsAny(fileName, LIBRARY_TOKENS))
                {
                    return false;
                }

                return true;
            })
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (candidates.Count == 0)
        {
            Console.WriteLine($"No matching artifacts found under {Sdl3BuildConstants.BUILD_DIR_NAME}.");
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
        var trimmed = TrimConfigSuffix(TrimVersionSuffix(baseName));
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

    private static string TrimVersionSuffix(string baseName)
    {
        const string SDL_LIB_PREFIX = "libSDL3.";
        const string SDL_PREFIX = "SDL3.";

        if (baseName.StartsWith(SDL_LIB_PREFIX, StringComparison.OrdinalIgnoreCase))
        {
            return "libSDL3";
        }

        if (baseName.StartsWith(SDL_PREFIX, StringComparison.OrdinalIgnoreCase))
        {
            return "SDL3";
        }

        return baseName;
    }
}

internal sealed record Options(string Config)
{
    public static Options Parse(string[] args)
    {
        var config = GlobalBuildUtils.DefaultConfig();

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

        if (config is not (GlobalBuildConstants.DEBUG_CONFIG or GlobalBuildConstants.RELEASE_CONFIG))
        {
            throw new ArgumentException("--config must be 'debug' or 'release'.");
        }

        return new Options(config);
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
