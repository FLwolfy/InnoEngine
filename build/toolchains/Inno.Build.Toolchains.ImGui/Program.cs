using System;
using System.IO;
using System.Linq;
using Inno.Build.Toolchains;
using Inno.Build.Toolchains.ImGui.Platforms;

namespace Inno.Build.Toolchains.ImGui;

static class Program
{
    private static readonly string[] LIBRARY_TOKENS = ["cimgui"];
    private static readonly string[] SHARED_EXTENSIONS = { ".dll", ".dylib", ".so" };

    /// <summary>
    /// Runs the command-line entry point and returns a process exit code.
    /// </summary>
    /// <param name="args">
    /// The command-line arguments that configure this invocation.
    /// </param>
    /// <returns>
    /// The scalar result calculated from the supplied inputs.
    /// </returns>
    public static int Main(string[] args)
    {
        if (args.Length == 0 || args[0] is not ("build" or "clean"))
        {
            Console.Error.WriteLine("Usage: Inno.Build.Toolchains.ImGui <build|clean> [--config debug|release]");
            return 2;
        }

        try
        {
            if (args[0] == "clean")
            {
                if (args.Length != 1)
                    throw new ArgumentException("The clean command does not accept additional arguments.");
                Clean();
                return 0;
            }

            var options = Options.Parse(args[1..]);
            var builder = CimguiBuilderFactory.CreateForCurrentPlatform();
            var repoRoot = ToolchainEnvironment.FindRepoRoot();
            var externDir = Path.Combine(repoRoot, ToolchainLayout.C_EXTERNAL_DIRECTORY_NAME);
            var cimguiDir = Path.Combine(externDir, CimguiBuildConstants.CIMGUI_DIR_NAME);
            var outputDir = Path.Combine(repoRoot, ToolchainLayout.C_OUTPUT_DIRECTORY_NAME, CimguiBuildConstants.OUTPUT_PRODUCT_DIR_NAME, builder.outputPlatform);

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

    private static void Clean()
    {
        string repositoryRoot = ToolchainEnvironment.FindRepoRoot();
        ToolchainEnvironment.DeleteDirectory(Path.Combine(
            repositoryRoot,
            ToolchainLayout.C_OUTPUT_DIRECTORY_NAME,
            CimguiBuildConstants.OUTPUT_PRODUCT_DIR_NAME));
        ToolchainEnvironment.DeleteDirectory(Path.Combine(
            repositoryRoot,
            ToolchainLayout.C_EXTERNAL_DIRECTORY_NAME,
            CimguiBuildConstants.CIMGUI_DIR_NAME,
            CimguiBuildConstants.BUILD_DIR_NAME));
        Console.WriteLine("ImGui outputs cleaned.");
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
    /// <summary>
    /// Parses validated input into the strongly typed state required by the caller.
    /// </summary>
    /// <param name="args">
    /// The command-line arguments that configure this invocation.
    /// </param>
    /// <returns>
    /// The validated options that represents the completed operation.
    /// </returns>
    public static Options Parse(string[] args)
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
