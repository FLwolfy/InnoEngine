using System;
using System.IO;
using System.Runtime.InteropServices;
using Inno.Build.Toolchains;

namespace Inno.Build.Toolchains.MiniAudio.Platforms;

internal sealed class LinuxMiniAudioBuilder : MiniAudioBuilder
{
    public override string OutputPlatform => RuntimeInformation.ProcessArchitecture switch
    {
        Architecture.X64 => "linux-x64",
        Architecture.Arm64 => "linux-arm64",
        _ => throw new PlatformNotSupportedException("miniaudio supports Linux x64 and ARM64 hosts.")
    };

    public override bool IsSupported() =>
        OperatingSystem.IsLinux() &&
        RuntimeInformation.ProcessArchitecture is Architecture.X64 or Architecture.Arm64;

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
