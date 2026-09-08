using System;
using System.IO;
using System.Linq;
using Inno.Build.Toolchains.Bgfx.Platforms;
using Inno.Build.Toolchains;

namespace Inno.Build.Toolchains.Bgfx;

internal static class BgfxNativeBuild
{
    private static readonly string[] LIBRARY_TOKENS =
    {
        BgfxBuildConstants.BGFX_DIR_NAME,
        BgfxBuildConstants.BX_DIR_NAME,
        BgfxBuildConstants.BIMG_DIR_NAME,
    };
    private static readonly string[] BUILD_PATH_TOKENS = { "/bin/", "/lib/" };
    private static readonly string[] SHARED_EXTENSIONS = { ".dll", ".dylib", ".so" };

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
            var options = NativeBuildOptions.Parse(args);
            var builder = BgfxBuilderFactory.CreateForCurrentPlatform();
            var repoRoot = ToolchainEnvironment.FindRepoRoot();
            var externDir = Path.Combine(repoRoot, ToolchainLayout.C_EXTERNAL_DIRECTORY_NAME);
            var bgfxDir = Path.Combine(externDir, BgfxBuildConstants.BGFX_DIR_NAME);
            var bxDir = Path.Combine(externDir, BgfxBuildConstants.BX_DIR_NAME);
            var bimgDir = Path.Combine(externDir, BgfxBuildConstants.BIMG_DIR_NAME);
            var outputPlatform = builder.outputPlatform;
            var outputDir = Path.Combine(repoRoot, ToolchainLayout.C_OUTPUT_DIRECTORY_NAME, BgfxBuildConstants.OUTPUT_PRODUCT_DIR_NAME, outputPlatform);

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
            ToolchainEnvironment.NormalizeOutputName);

        BuildArtifactCopier.CopyArtifacts(bgfxDir, outputDir, config, options);
    }

}

internal sealed record NativeBuildOptions(
    string makeTargetOverride,
    bool includeStatic,
    string config)
{
    /// <summary>
    /// Parses validated input into the strongly typed state required by the caller.
    /// </summary>
    /// <param name="args">
    /// The command-line arguments that configure this invocation.
    /// </param>
    /// <returns>
    /// The validated native build options that represents the completed operation.
    /// </returns>
    public static NativeBuildOptions Parse(string[] args)
    {
        var config = ToolchainEnvironment.DefaultConfig();
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

        if (config is not (ToolchainLayout.C_DEBUG_CONFIGURATION or ToolchainLayout.C_RELEASE_CONFIGURATION))
        {
            throw new ArgumentException("--config must be 'debug' or 'release'.");
        }

        return new NativeBuildOptions(makeTargetOverride, includeStatic, config);
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
