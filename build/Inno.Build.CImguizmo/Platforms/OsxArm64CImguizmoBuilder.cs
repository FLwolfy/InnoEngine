using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Inno.Build.Global;

namespace Inno.Build.CImguizmo.Platforms;

public sealed class OsxArm64CImguizmoBuilder : CImguizmoBuilder
{
    private const string OUTPUT_PLATFORM = "osx-arm64";
    private const string BUILD_DIR_NAME = "osx-arm64";

    public override string outputPlatform => OUTPUT_PLATFORM;

    public override bool IsSupported()
    {
        return RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
            && RuntimeInformation.ProcessArchitecture == Architecture.Arm64;
    }

    public override void Build(string cimguizmoDir, string cimguiDir, string cimguiBuildDir, string cimguiOutputDir, string config)
    {
        var buildDir = Path.Combine(cimguizmoDir, CImguizmoBuildConstants.BUILD_DIR_NAME, BUILD_DIR_NAME);
        Directory.CreateDirectory(buildDir);

        var cimguizmoCpp = Path.Combine(cimguizmoDir, CImguizmoBuildConstants.CIMGUIMO_CPP_FILE);
        var imguizmoCpp = Path.Combine(cimguizmoDir, CImguizmoBuildConstants.IMGUIZMO_DIR_NAME, CImguizmoBuildConstants.IMGUIZMO_CPP_FILE);
        var cimguiLib = FindCimguiLibrary(cimguiOutputDir, config);
        var outputLib = Path.Combine(buildDir, $"{CImguizmoBuildConstants.OUTPUT_DLL_NAME}.dylib");

        var includes = new[]
        {
            cimguizmoDir,
            Path.Combine(cimguizmoDir, CImguizmoBuildConstants.IMGUIZMO_DIR_NAME),
            cimguiDir,
            Path.Combine(cimguiDir, "imgui"),
        };

        var includeArgs = string.Join(" ", includes.Select(path => $"-I\"{path}\""));
        var cflags = config == GlobalBuildConstants.DEBUG_CONFIG ? "-O0 -g" : "-O3";
        var rpath = "@loader_path/../cimgui/osx-arm64";
        var installName = $"@rpath/{CImguizmoBuildConstants.OUTPUT_DLL_NAME}.dylib";
        var args = $"{cflags} -std=c++11 -fPIC -dynamiclib {includeArgs} \"{cimguizmoCpp}\" \"{imguizmoCpp}\" \"{cimguiLib}\" -Wl,-install_name,{installName} -Wl,-rpath,{rpath} -o \"{outputLib}\"";

        GlobalBuildUtils.Run("clang++", args, cimguizmoDir);
    }

    private static string FindCimguiLibrary(string cimguiOutputDir, string config)
    {
        var name = config == GlobalBuildConstants.DEBUG_CONFIG
            ? "libcimgui-debug.dylib"
            : "libcimgui-release.dylib";
        var path = Path.Combine(cimguiOutputDir, name);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"cimgui shared library not found: {path}");
        }

        return path;
    }
}
