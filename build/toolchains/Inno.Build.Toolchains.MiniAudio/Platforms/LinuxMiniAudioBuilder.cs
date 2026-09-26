using System;
using System.IO;
using System.Runtime.InteropServices;
using Inno.Build.Toolchains;

namespace Inno.Build.Toolchains.MiniAudio.Platforms;

internal sealed class LinuxMiniAudioBuilder : MiniAudioBuilder
{
    /// <summary>
    /// Gets the output platform text used by the current instance.
    /// </summary>
public override string OutputPlatform => RuntimeInformation.ProcessArchitecture switch
    {
        Architecture.X64 => "linux-x64",
        Architecture.Arm64 => "linux-arm64",
        _ => throw new PlatformNotSupportedException("miniaudio supports Linux x64 and ARM64 hosts.")
    };

    /// <summary>
    /// Determines whether the current host can execute this implementation.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> when the documented condition is satisfied; otherwise, <see langword="false"/>.
    /// </returns>
public override bool IsSupported() =>
        OperatingSystem.IsLinux() &&
        RuntimeInformation.ProcessArchitecture is Architecture.X64 or Architecture.Arm64;

    /// <summary>
    /// Builds a validated result from the current immutable input snapshot.
    /// </summary>
    /// <param name="miniAudioDirectory">
    /// The mini audio directory text validated by the build operation.
    /// </param>
    /// <param name="config">
    /// The validated configuration that controls this operation.
    /// </param>
public override void Build(string miniAudioDirectory, string config)
    {
        string buildDirectory = Path.Combine(
            miniAudioDirectory,
            MiniAudioBuildConstants.BUILD_DIR_NAME,
            OutputPlatform,
            config);
        string buildType = GetBuildType(config);
        string commonOptions = GetCommonCMakeOptions("-DMA_DLL");

        ToolchainEnvironment.Run(
            "cmake",
            $"-S . -B \"{buildDirectory}\" -DCMAKE_BUILD_TYPE={buildType} {commonOptions}",
            miniAudioDirectory);
        ToolchainEnvironment.Run(
            "cmake",
            $"--build \"{buildDirectory}\" --config {buildType}",
            miniAudioDirectory);
    }
}
