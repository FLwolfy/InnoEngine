using System;
using System.IO;
using System.Linq;
using Inno.Build.Bgfx.Platforms;
using Inno.Build.Global;

namespace Inno.Build.Bgfx;

static class Program
{
    private static readonly string[] LIBRARY_TOKENS =
    {
        BgfxBuildConstants.BGFX_DIR_NAME,
        BgfxBuildConstants.BX_DIR_NAME,
        BgfxBuildConstants.BIMG_DIR_NAME,
    };
    private static readonly string[] BUILD_PATH_TOKENS = { "/bin/", "/lib/" };
    private static readonly string[] SHARED_EXTENSIONS = { ".dll", ".dylib", ".so" };

    public static int Main(string[] args)
    {
        try
        {
            var options = Options.Parse(args);
            var builder = BgfxBuilderFactory.CreateForCurrentPlatform();
            var repoRoot = GlobalBuildUtils.FindRepoRoot();
            var externDir = Path.Combine(repoRoot, GlobalBuildConstants.EXTERN_DIR_NAME);
            var bgfxDir = Path.Combine(externDir, BgfxBuildConstants.BGFX_DIR_NAME);
            var bxDir = Path.Combine(externDir, BgfxBuildConstants.BX_DIR_NAME);
            var bimgDir = Path.Combine(externDir, BgfxBuildConstants.BIMG_DIR_NAME);
            var outputPlatform = builder.outputPlatform;
            var outputDir = Path.Combine(repoRoot, GlobalBuildConstants.OUTPUT_ROOT_DIR_NAME, BgfxBuildConstants.OUTPUT_PRODUCT_DIR_NAME, outputPlatform);

            Directory.CreateDirectory(externDir);
            Directory.CreateDirectory(outputDir);

            BgfxBuildUtils.ValidateSubmodules(bgfxDir, bxDir, bimgDir);

            builder.Build(bgfxDir, options.config, options.makeTargetOverride);

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
        var extensions = includeStatic
            ? SHARED_EXTENSIONS.Concat(new[] { ".a", ".lib" }).ToArray()
            : SHARED_EXTENSIONS;

        var options = new BuildArtifactOptions(
            BgfxBuildConstants.BUILD_DIR_NAME,
            LIBRARY_TOKENS,
            extensions,
            BUILD_PATH_TOKENS,
            GlobalBuildUtils.NormalizeOutputName);

        BuildArtifactCopier.CopyArtifacts(bgfxDir, outputDir, config, options);
    }

}

internal sealed record Options(
    string makeTargetOverride,
    bool includeStatic,
    string config)
{
    public static Options Parse(string[] args)
    {
        var config = GlobalBuildUtils.DefaultConfig();
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

        if (config is not (GlobalBuildConstants.DEBUG_CONFIG or GlobalBuildConstants.RELEASE_CONFIG))
        {
            throw new ArgumentException("--config must be 'debug' or 'release'.");
        }

        return new Options(makeTargetOverride, includeStatic, config);
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
