using System;
using System.IO;
using System.Linq;
using Inno.Build.Toolchains;
using Inno.Build.Toolchains.Sdl3.Platforms;

namespace Inno.Build.Toolchains.Sdl3;

static class Program
{
    private static readonly string[] LIBRARY_TOKENS = ["SDL3"];
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
            Console.Error.WriteLine("Usage: Inno.Build.Toolchains.Sdl3 <build|clean> [--config debug|release]");
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
            var builder = Sdl3BuilderFactory.CreateForCurrentPlatform();
            var repoRoot = ToolchainEnvironment.FindRepoRoot();
            var externDir = Path.Combine(repoRoot, ToolchainLayout.C_EXTERNAL_DIRECTORY_NAME);
            var sdlDir = Path.Combine(externDir, Sdl3BuildConstants.SDL_DIR_NAME);
            var outputDir = Path.Combine(repoRoot, ToolchainLayout.C_OUTPUT_DIRECTORY_NAME, Sdl3BuildConstants.OUTPUT_PRODUCT_DIR_NAME, builder.OutputPlatform);

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

    private static void Clean()
    {
        string repositoryRoot = ToolchainEnvironment.FindRepoRoot();
        ToolchainEnvironment.DeleteDirectory(Path.Combine(
            repositoryRoot,
            ToolchainLayout.C_OUTPUT_DIRECTORY_NAME,
            Sdl3BuildConstants.OUTPUT_PRODUCT_DIR_NAME));
        ToolchainEnvironment.DeleteDirectory(Path.Combine(
            repositoryRoot,
            ToolchainLayout.C_EXTERNAL_DIRECTORY_NAME,
            Sdl3BuildConstants.SDL_DIR_NAME,
            Sdl3BuildConstants.BUILD_DIR_NAME));
        Console.WriteLine("SDL3 outputs cleaned.");
    }

    private static void CopyArtifacts(string sdlDir, string outputDir, string config)
    {
        var options = new BuildArtifactOptions(
            Sdl3BuildConstants.BUILD_DIR_NAME,
            LIBRARY_TOKENS,
            SHARED_EXTENSIONS,
            null,
            NormalizeOutputName);

        BuildArtifactCopier.CopyArtifacts(sdlDir, outputDir, config, options);
    }

    private static string NormalizeOutputName(string fileName, string config)
    {
        var ext = Path.GetExtension(fileName);
        return $"{Sdl3BuildConstants.OUTPUT_DLL_NAME}-{config}{ext}";
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
