using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Inno.Build.Toolchains;

namespace Inno.Build.Toolchains.ImGuizmo.Platforms;

internal sealed class LinuxCImguizmoBuilder : CImguizmoBuilder
{
    private const string THIRD_PARTY_WARNING_POLICY = "-Werror -Wno-deprecated-declarations";

    public override string outputPlatform => RuntimeInformation.ProcessArchitecture switch
    {
        Architecture.X64 => "linux-x64",
        Architecture.Arm64 => "linux-arm64",
        _ => throw new PlatformNotSupportedException("cimguizmo supports Linux x64 and ARM64 hosts.")
    };

    public override bool IsSupported() =>
        OperatingSystem.IsLinux() &&
        RuntimeInformation.ProcessArchitecture is Architecture.X64 or Architecture.Arm64;

    public override void Build(
        string cimguizmoDir,
        string cimguiDir,
        string cimguiBuildDir,
        string cimguiOutputDir,
        string config)
    {
        string buildDir = Path.Combine(cimguizmoDir, CImguizmoBuildConstants.BUILD_DIR_NAME, outputPlatform);
        Directory.CreateDirectory(buildDir);

        string cimguizmoCpp = Path.Combine(cimguizmoDir, CImguizmoBuildConstants.CIMGUIMO_CPP_FILE);
        string imguizmoCpp = Path.Combine(cimguizmoDir, CImguizmoBuildConstants.IMGUIZMO_DIR_NAME, CImguizmoBuildConstants.IMGUIZMO_CPP_FILE);
        string cimguiLib = FindCimguiLibrary(cimguiOutputDir, config);
        string outputLib = Path.Combine(buildDir, $"{CImguizmoBuildConstants.OUTPUT_DLL_NAME}.so");
        string[] includes =
        [
            cimguizmoDir,
            Path.Combine(cimguizmoDir, CImguizmoBuildConstants.IMGUIZMO_DIR_NAME),
            cimguiDir,
            Path.Combine(cimguiDir, "imgui")
        ];
        string includeArgs = string.Join(" ", includes.Select(path => $"-I\"{path}\""));
        string cflags = config == ToolchainLayout.C_DEBUG_CONFIGURATION ? "-O0 -g" : "-O3";
        string relativeRPath = $"$ORIGIN/../../cimgui/{outputPlatform}";
        string args = $"{cflags} {THIRD_PARTY_WARNING_POLICY} -std=c++11 -fPIC -shared {includeArgs} \"{cimguizmoCpp}\" \"{imguizmoCpp}\" \"{cimguiLib}\" -Wl,-soname,{CImguizmoBuildConstants.OUTPUT_DLL_NAME}.so -Wl,-rpath,{relativeRPath} -o \"{outputLib}\"";
        ToolchainEnvironment.Run("clang++", args, cimguizmoDir);
    }

    private static string FindCimguiLibrary(string cimguiOutputDir, string config)
    {
        string path = Path.Combine(cimguiOutputDir, $"libcimgui-{config}.so");
        if (!File.Exists(path))
            throw new FileNotFoundException($"cimgui shared library not found: {path}", path);
        return path;
    }
}
