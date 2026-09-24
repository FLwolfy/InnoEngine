using System;
using System.IO;
using System.Runtime.InteropServices;
using Inno.Build.Toolchains;

namespace Inno.Build.Toolchains.ImGui.Platforms;

internal sealed class LinuxCimguiBuilder : CimguiBuilder
{
    public override string outputPlatform => RuntimeInformation.ProcessArchitecture switch
    {
        Architecture.X64 => "linux-x64",
        Architecture.Arm64 => "linux-arm64",
        _ => throw new PlatformNotSupportedException("cimgui supports Linux x64 and ARM64 hosts.")
    };

    public override bool IsSupported() =>
        OperatingSystem.IsLinux() &&
        RuntimeInformation.ProcessArchitecture is Architecture.X64 or Architecture.Arm64;

    public override void Build(string cimguiDir, string config)
    {
        string buildDir = Path.Combine(cimguiDir, CimguiBuildConstants.BUILD_DIR_NAME, "inno", outputPlatform);
        string buildType = GetBuildType(config);
        string sourceDir = CimguiSourceOverlay.Prepare(cimguiDir);

        ToolchainEnvironment.Run(
            "cmake",
            $"-S \"{sourceDir}\" -B \"{buildDir}\" -DINNO_CIMGUI_SOURCE_DIR=\"{cimguiDir}\" -DCMAKE_BUILD_TYPE={buildType} -DBUILD_SHARED_LIBS=ON -DCIMGUI_VARGS0=ON",
            cimguiDir);
        ToolchainEnvironment.Run(
            "cmake",
            $"--build \"{buildDir}\" --config {buildType}",
            cimguiDir);
    }
}
