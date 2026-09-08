using System.IO;
using System.Runtime.InteropServices;
using Inno.Build.Toolchains;

namespace Inno.Build.Toolchains.Sdl3.Platforms;

internal sealed class WindowsX64Sdl3Builder : Sdl3Builder
{
    private const string OUTPUT_PLATFORM = "windows-x64";
    private const string BUILD_DIR_NAME = "windows-x64";
    private const string GENERATOR = "Visual Studio 17 2022";
    private const string PLATFORM = "x64";

    /// <summary>
    /// Gets text used for stable identity, presentation, or diagnostics by this contract.
    /// </summary>
    public override string OutputPlatform => OUTPUT_PLATFORM;

    /// <summary>
    /// Determines whether the current host can execute this implementation.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> when the requested condition is satisfied; otherwise, <see langword="false"/>.
    /// </returns>
    public override bool IsSupported()
    {
        return RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            && RuntimeInformation.ProcessArchitecture == Architecture.X64;
    }

    /// <summary>
    /// Builds a validated result from the current immutable input snapshot.
    /// </summary>
    /// <param name="sdlDir">
    /// The sdl dir text validated by the build operation.
    /// </param>
    /// <param name="config">
    /// The validated configuration that controls this operation.
    /// </param>
    public override void Build(string sdlDir, string config)
    {
        var buildDir = Path.Combine(sdlDir, Sdl3BuildConstants.BUILD_DIR_NAME, BUILD_DIR_NAME);
        var buildType = GetBuildType(config);

        ToolchainEnvironment.Run("cmake", $"-S . -B \"{buildDir}\" -G \"{GENERATOR}\" -A {PLATFORM} -DSDL_SHARED=ON -DSDL_STATIC=OFF", sdlDir);
        ToolchainEnvironment.Run("cmake", $"--build \"{buildDir}\" --config {buildType}", sdlDir);
    }
}
