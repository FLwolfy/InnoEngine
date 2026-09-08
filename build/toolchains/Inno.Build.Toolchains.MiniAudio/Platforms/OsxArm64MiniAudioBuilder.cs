using System.IO;
using System.Runtime.InteropServices;
using Inno.Build.Toolchains;

namespace Inno.Build.Toolchains.MiniAudio.Platforms;

internal sealed class OsxArm64MiniAudioBuilder : MiniAudioBuilder
{
    private const string OUTPUT_PLATFORM = "osx-arm64";
    private const string BUILD_DIR_NAME = "osx-arm64";

    /// <summary>
    /// Gets the macOS ARM64 runtime identifier produced by this builder.
    /// </summary>
    public override string OutputPlatform => OUTPUT_PLATFORM;

    /// <summary>
    /// Determines whether the current process is running on macOS ARM64.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> when this builder can execute on the current host.
    /// </returns>
    public override bool IsSupported()
    {
        return RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
            && RuntimeInformation.ProcessArchitecture == Architecture.Arm64;
    }

    /// <summary>
    /// Builds the pinned miniaudio source as a macOS ARM64 dynamic library.
    /// </summary>
    /// <param name="miniAudioDirectory">
    /// The absolute path of the validated miniaudio source checkout.
    /// </param>
    /// <param name="config">
    /// The normalized debug or release configuration token.
    /// </param>
    public override void Build(string miniAudioDirectory, string config)
    {
        string buildDirectory = Path.Combine(
            miniAudioDirectory,
            MiniAudioBuildConstants.BUILD_DIR_NAME,
            BUILD_DIR_NAME,
            config);
        string buildType = GetBuildType(config);
        string commonOptions = GetCommonCMakeOptions("-DMA_DLL");

        ToolchainEnvironment.Run(
            "cmake",
            $"-S . -B \"{buildDirectory}\" -DCMAKE_BUILD_TYPE={buildType} -DCMAKE_OSX_ARCHITECTURES=arm64 {commonOptions}",
            miniAudioDirectory);
        ToolchainEnvironment.Run(
            "cmake",
            $"--build \"{buildDirectory}\" --config {buildType}",
            miniAudioDirectory);
    }
}
