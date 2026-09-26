using System;
using System.IO;
using System.Runtime.InteropServices;
using Inno.Build.Toolchains;

namespace Inno.Build.Toolchains.Sdl3.Platforms;

internal sealed class LinuxSdl3Builder : Sdl3Builder
{
    /// <summary>
    /// Gets the output platform text used by the current instance.
    /// </summary>
public override string OutputPlatform => RuntimeInformation.ProcessArchitecture switch
    {
        Architecture.X64 => "linux-x64",
        Architecture.Arm64 => "linux-arm64",
        _ => throw new PlatformNotSupportedException("SDL3 supports Linux x64 and ARM64 hosts.")
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
    /// <param name="sdlDir">
    /// The sdl dir text validated by the build operation.
    /// </param>
    /// <param name="config">
    /// The validated configuration that controls this operation.
    /// </param>
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
