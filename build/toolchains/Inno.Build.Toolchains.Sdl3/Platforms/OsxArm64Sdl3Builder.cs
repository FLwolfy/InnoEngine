using System.IO;
using System.Runtime.InteropServices;
using Inno.Build.Toolchains;

namespace Inno.Build.Toolchains.Sdl3.Platforms;

internal sealed class OsxArm64Sdl3Builder : Sdl3Builder
{
    private const string OUTPUT_PLATFORM = "osx-arm64";
    private const string BUILD_DIR_NAME = "osx-arm64";

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
        return RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
            && RuntimeInformation.ProcessArchitecture == Architecture.Arm64;
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

        ToolchainEnvironment.Run("cmake", $"-S . -B \"{buildDir}\" -DCMAKE_BUILD_TYPE={buildType} -DSDL_SHARED=ON -DSDL_STATIC=OFF", sdlDir);
        ToolchainEnvironment.Run("cmake", $"--build \"{buildDir}\" --config {buildType}", sdlDir);
    }
}
