using System;
using System.IO;
using Inno.Build.Toolchains;
using Inno.Build.Toolchains.MiniAudio.Platforms;

namespace Inno.Build.Toolchains.MiniAudio;

internal static class Program
{
    private static readonly string[] S_LIBRARY_TOKENS = ["miniaudio"];
    private static readonly string[] S_SHARED_EXTENSIONS = [".dll", ".dylib", ".so"];

    /// <summary>
    /// Builds or cleans the pinned miniaudio shared-library outputs.
    /// </summary>
    /// <param name="arguments">
    /// The command name followed by an optional debug or release configuration.
    /// </param>
    /// <returns>
    /// Zero on success, two for invalid usage, or one when the requested operation fails.
    /// </returns>
    public static int Main(string[] arguments)
    {
        if (arguments.Length == 0 || arguments[0] is not ("build" or "clean"))
        {
            Console.Error.WriteLine(
                "Usage: Inno.Build.Toolchains.MiniAudio <build|clean> [--config debug|release]");
            return 2;
        }

        try
        {
            if (arguments[0] == "clean")
            {
                if (arguments.Length != 1)
                    throw new ArgumentException("The clean command does not accept additional arguments.");
                Clean();
                return 0;
            }

            Options options = Options.Parse(arguments[1..]);
            MiniAudioBuilder builder = MiniAudioBuilderFactory.CreateForCurrentPlatform();
            string repositoryRoot = ToolchainEnvironment.FindRepoRoot();
            string miniAudioDirectory = Path.Combine(
                repositoryRoot,
                ToolchainLayout.C_EXTERNAL_DIRECTORY_NAME,
                MiniAudioBuildConstants.MINIAUDIO_DIR_NAME);
            string outputDirectory = Path.Combine(
                repositoryRoot,
                ToolchainLayout.C_OUTPUT_DIRECTORY_NAME,
                MiniAudioBuildConstants.OUTPUT_PRODUCT_DIR_NAME,
                builder.OutputPlatform);

            MiniAudioBuildUtils.ValidateSource(miniAudioDirectory);
            Directory.CreateDirectory(outputDirectory);
            string expectedOutput = Path.Combine(
                outputDirectory,
                GetExpectedOutputFileName(builder.OutputPlatform, options.Config));
            if (File.Exists(expectedOutput))
                File.Delete(expectedOutput);
            builder.Build(miniAudioDirectory, options.Config);
            CopyArtifacts(
                miniAudioDirectory,
                outputDirectory,
                builder.OutputPlatform,
                options.Config);
            if (!File.Exists(expectedOutput))
            {
                throw new FileNotFoundException(
                    "The miniaudio build completed without producing the required shared library.",
                    expectedOutput);
            }

            Console.WriteLine($"miniaudio build complete. Output: {outputDirectory}");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
    }

    private static void Clean()
    {
        string repositoryRoot = ToolchainEnvironment.FindRepoRoot();
        ToolchainEnvironment.DeleteDirectory(Path.Combine(
            repositoryRoot,
            ToolchainLayout.C_OUTPUT_DIRECTORY_NAME,
            MiniAudioBuildConstants.OUTPUT_PRODUCT_DIR_NAME));
        ToolchainEnvironment.DeleteDirectory(Path.Combine(
            repositoryRoot,
            ToolchainLayout.C_EXTERNAL_DIRECTORY_NAME,
            MiniAudioBuildConstants.MINIAUDIO_DIR_NAME,
            MiniAudioBuildConstants.BUILD_DIR_NAME));
        Console.WriteLine("miniaudio outputs cleaned.");
    }

    private static void CopyArtifacts(
        string miniAudioDirectory,
        string outputDirectory,
        string outputPlatform,
        string config)
    {
        var options = new BuildArtifactOptions(
            MiniAudioBuildConstants.BUILD_DIR_NAME,
            S_LIBRARY_TOKENS,
            S_SHARED_EXTENSIONS,
            [$"/{outputPlatform}/{config}/"],
            ToolchainEnvironment.NormalizeOutputName);
        BuildArtifactCopier.CopyArtifacts(miniAudioDirectory, outputDirectory, config, options);
    }

    private static string GetExpectedOutputFileName(string outputPlatform, string config)
    {
        return outputPlatform switch
        {
            "osx-arm64" => $"libminiaudio-{config}.dylib",
            "windows-x64" => $"miniaudio-{config}.dll",
            _ => throw new PlatformNotSupportedException(
                $"No miniaudio output name is defined for '{outputPlatform}'.")
        };
    }
}

internal sealed record Options(string Config)
{
    /// <summary>
    /// Parses command-line arguments into a validated native build configuration.
    /// </summary>
    /// <param name="arguments">
    /// The optional configuration arguments following the build command.
    /// </param>
    /// <returns>
    /// The validated debug or release build options.
    /// </returns>
    public static Options Parse(string[] arguments)
    {
        string config = ToolchainEnvironment.DefaultConfig();
        for (int index = 0; index < arguments.Length; index++)
        {
            switch (arguments[index])
            {
                case "--config":
                    config = GetNext(arguments, ref index).ToLowerInvariant();
                    break;
                default:
                    throw new ArgumentException($"Unknown argument: {arguments[index]}");
            }
        }

        if (config is not (ToolchainLayout.C_DEBUG_CONFIGURATION or ToolchainLayout.C_RELEASE_CONFIGURATION))
            throw new ArgumentException("--config must be 'debug' or 'release'.");
        return new Options(config);
    }

    private static string GetNext(string[] arguments, ref int index)
    {
        if (index + 1 >= arguments.Length)
            throw new ArgumentException($"Missing value for {arguments[index]}.");
        index++;
        return arguments[index];
    }
}
