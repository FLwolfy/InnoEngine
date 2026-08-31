using System;
using System.IO;
using System.Linq;
using Inno.Build.Global;
using Inno.Build.Cimgui.Platforms;

namespace Inno.Build.Cimgui;

static class Program
{
    private static readonly string[] LIBRARY_TOKENS = ["cimgui"];
    private static readonly string[] SHARED_EXTENSIONS = { ".dll", ".dylib", ".so" };

    public static int Main(string[] args)
    {
        try
        {
            var options = Options.Parse(args);
            var builder = CimguiBuilderFactory.CreateForCurrentPlatform();
            var repoRoot = GlobalBuildUtils.FindRepoRoot();
            var externDir = Path.Combine(repoRoot, GlobalBuildConstants.EXTERN_DIR_NAME);
            var cimguiDir = Path.Combine(externDir, CimguiBuildConstants.CIMGUI_DIR_NAME);
            var outputDir = Path.Combine(repoRoot, GlobalBuildConstants.OUTPUT_ROOT_DIR_NAME, CimguiBuildConstants.OUTPUT_PRODUCT_DIR_NAME, builder.outputPlatform);

            Directory.CreateDirectory(externDir);
            Directory.CreateDirectory(outputDir);

            CimguiBuildUtils.ValidateSource(cimguiDir);

            builder.Build(cimguiDir, options.Config);
            CopyArtifacts(cimguiDir, outputDir, options.Config);

            Console.WriteLine($"cimgui build complete. Output: {outputDir}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static void CopyArtifacts(string cimguiDir, string outputDir, string config)
    {
        var options = new BuildArtifactOptions(
            CimguiBuildConstants.BUILD_DIR_NAME,
            LIBRARY_TOKENS,
            SHARED_EXTENSIONS,
            null,
            NormalizeOutputName);

        BuildArtifactCopier.CopyArtifacts(cimguiDir, outputDir, config, options);
    }

    private static string NormalizeOutputName(string fileName, string config)
    {
        var ext = Path.GetExtension(fileName);
        return $"{CimguiBuildConstants.OUTPUT_DLL_NAME}-{config}{ext}";
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
