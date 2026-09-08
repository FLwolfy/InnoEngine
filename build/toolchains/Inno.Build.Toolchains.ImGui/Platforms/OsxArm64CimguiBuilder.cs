using System.IO;
using System.Runtime.InteropServices;
using Inno.Build.Toolchains;

namespace Inno.Build.Toolchains.ImGui.Platforms;

internal sealed class OsxArm64CimguiBuilder : CimguiBuilder
{
    private const string OUTPUT_PLATFORM = "osx-arm64";
    private const string BUILD_DIR_NAME = "osx-arm64";

    /// <summary>
    /// Gets the native platform identifier produced by this builder.
    /// </summary>
    public override string outputPlatform => OUTPUT_PLATFORM;

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
    /// <param name="cimguiDir">
    /// The cimgui dir text validated by the build operation.
    /// </param>
    /// <param name="config">
    /// The validated configuration that controls this operation.
    /// </param>
    public override void Build(string cimguiDir, string config)
    {
        var buildDir = Path.Combine(cimguiDir, CimguiBuildConstants.BUILD_DIR_NAME, BUILD_DIR_NAME);
        var buildType = GetBuildType(config);

        ToolchainEnvironment.Run("cmake", $"-S . -B \"{buildDir}\" -DCMAKE_BUILD_TYPE={buildType} -DBUILD_SHARED_LIBS=ON", cimguiDir);
        ToolchainEnvironment.Run("cmake", $"--build \"{buildDir}\" --config {buildType}", cimguiDir);
    }
}
