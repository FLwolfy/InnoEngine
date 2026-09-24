using System;
using System.IO;
using System.Runtime.InteropServices;
using Inno.Build.Toolchains;

namespace Inno.Build.Toolchains.Sdl3.Platforms;

internal sealed class LinuxSdl3Builder : Sdl3Builder
{
    public override string OutputPlatform => RuntimeInformation.ProcessArchitecture switch
    {
        Architecture.X64 => "linux-x64",
        Architecture.Arm64 => "linux-arm64",
        _ => throw new PlatformNotSupportedException("SDL3 supports Linux x64 and ARM64 hosts.")
    };

    public override bool IsSupported() =>
        OperatingSystem.IsLinux() &&
        RuntimeInformation.ProcessArchitecture is Architecture.X64 or Architecture.Arm64;

    public override void Build(string sdlDir, string config)
    {
        string buildDir = Path.Combine(sdlDir, Sdl3BuildConstants.BUILD_DIR_NAME, OutputPlatform);
        string buildType = GetBuildType(config);
        ToolchainEnvironment.Run(
            "cmake",
            $"-S . -B \"{buildDir}\" -DCMAKE_BUILD_TYPE={buildType} -DSDL_SHARED=ON -DSDL_STATIC=OFF -DSDL_TESTS=OFF -DSDL_EXAMPLES=OFF",
            sdlDir);
        ToolchainEnvironment.Run(
            "cmake",
            $"--build \"{buildDir}\" --config {buildType} --target SDL3-shared",
            sdlDir);
    }
}
