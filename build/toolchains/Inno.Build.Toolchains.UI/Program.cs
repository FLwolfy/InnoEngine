using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

using Inno.Build.Toolchains;

namespace Inno.Build.Toolchains.UI;

internal static class Program
{
    /// <summary>
    /// Builds or cleans the native UI library owned by Inno.Native.UI.
    /// </summary>
    /// <param name="arguments">
    /// The command and optional build configuration.
    /// </param>
    /// <returns>
    /// Zero on success, two for invalid usage, or one when the native operation fails.
    /// </returns>
    public static int Main(string[] arguments)
    {
        if (arguments.Length == 0 || arguments[0] is not ("build" or "clean"))
        {
            Console.Error.WriteLine("Usage: Inno.Build.Toolchains.UI <build|clean> [--config debug|release]");
            return 2;
        }
        try
        {
            string repositoryRoot = ToolchainEnvironment.FindRepoRoot();
            string nativeDirectory = Path.Combine(
                repositoryRoot,
                "native",
                "Inno.Native.UI",
                "Native");
            string platform = ResolvePlatform();
            string buildRoot = Path.Combine(
                repositoryRoot,
                "build",
                "toolchains",
                "Inno.Build.Toolchains.UI",
                "obj",
                "native",
                platform);
            string outputRoot = Path.Combine(repositoryRoot, ToolchainLayout.C_OUTPUT_DIRECTORY_NAME, "ui", platform);
            if (arguments[0] == "clean")
            {
                if (arguments.Length != 1)
                    throw new ArgumentException("The clean command does not accept additional arguments.");
                ToolchainEnvironment.DeleteDirectory(buildRoot);
                ToolchainEnvironment.DeleteDirectory(outputRoot);
                Console.WriteLine("UI native outputs cleaned.");
                return 0;
            }
            string config = ParseConfig(arguments[1..]);
            string buildType = config == ToolchainLayout.C_DEBUG_CONFIGURATION ? "Debug" : "Release";
            string buildDirectory = Path.Combine(buildRoot, config);
            string architectureOptions = OperatingSystem.IsMacOS()
                ? "-DCMAKE_OSX_ARCHITECTURES=arm64"
                : string.Empty;
            string generatorOptions = OperatingSystem.IsWindows()
                ? "-G \"Visual Studio 17 2022\" -A x64"
                : $"-DCMAKE_BUILD_TYPE={buildType}";
            ToolchainEnvironment.Run(
                "cmake",
                $"-S \"{nativeDirectory}\" -B \"{buildDirectory}\" {generatorOptions} {architectureOptions}",
                repositoryRoot);
            ToolchainEnvironment.Run(
                "cmake",
                $"--build \"{buildDirectory}\" --config {buildType} --target inno-ui",
                repositoryRoot);
            string source = FindLibrary(buildDirectory, buildType);
            Directory.CreateDirectory(outputRoot);
            string outputName = ToolchainEnvironment.NormalizeOutputName(Path.GetFileName(source), config);
            string destination = Path.Combine(outputRoot, outputName);
            File.Copy(source, destination, overwrite: true);
            Console.WriteLine($"UI native build complete. Output: {destination}");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
    }

    private static string ParseConfig(string[] arguments)
    {
        string config = ToolchainEnvironment.DefaultConfig();
        for (int index = 0; index < arguments.Length; index++)
        {
            if (arguments[index] != "--config" || index + 1 >= arguments.Length)
                throw new ArgumentException($"Unknown or incomplete argument '{arguments[index]}'.");
            config = arguments[++index].ToLowerInvariant();
        }
        if (config is not (ToolchainLayout.C_DEBUG_CONFIGURATION or ToolchainLayout.C_RELEASE_CONFIGURATION))
            throw new ArgumentException("--config must be 'debug' or 'release'.");
        return config;
    }

    private static string ResolvePlatform()
    {
        Architecture architecture = RuntimeInformation.ProcessArchitecture;
        if (OperatingSystem.IsMacOS() && architecture == Architecture.Arm64)
            return "osx-arm64";
        if (OperatingSystem.IsWindows() && architecture == Architecture.X64)
            return "windows-x64";
        if (OperatingSystem.IsLinux() && architecture == Architecture.X64)
            return "linux-x64";
        if (OperatingSystem.IsLinux() && architecture == Architecture.Arm64)
            return "linux-arm64";
        throw new PlatformNotSupportedException("UI supports macOS ARM64, Windows x64, and Linux x64/ARM64 hosts.");
    }

    private static string FindLibrary(string buildDirectory, string buildType)
    {
        string expected = OperatingSystem.IsWindows() ? "inno-ui.dll"
            : OperatingSystem.IsMacOS() ? "libinno-ui.dylib"
            : "libinno-ui.so";
        string[] candidates = Directory.GetFiles(buildDirectory, expected, SearchOption.AllDirectories)
            .Where(path => !path.Contains("CMakeFiles", StringComparison.Ordinal))
            .Where(path => !OperatingSystem.IsWindows()
                || path.Contains(Path.DirectorySeparatorChar + buildType + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        return candidates.Length == 1
            ? candidates[0]
            : throw new FileNotFoundException($"Expected exactly one '{expected}' output but found {candidates.Length}.");
    }
}
