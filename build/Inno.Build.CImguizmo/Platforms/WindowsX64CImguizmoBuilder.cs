using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Inno.Build.Global;

namespace Inno.Build.CImguizmo.Platforms;

public sealed class WindowsX64CImguizmoBuilder : CImguizmoBuilder
{
    private const string OUTPUT_PLATFORM = "windows-x64";
    private const string BUILD_DIR_NAME = "windows-x64";

    public override string outputPlatform => OUTPUT_PLATFORM;

    public override bool IsSupported()
    {
        return RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            && RuntimeInformation.ProcessArchitecture == Architecture.X64;
    }

    public override void Build(string cimguizmoDir, string cimguiDir, string cimguiBuildDir, string cimguiOutputDir, string config)
    {
        var buildDir = Path.Combine(cimguizmoDir, CImguizmoBuildConstants.BUILD_DIR_NAME, BUILD_DIR_NAME);
        Directory.CreateDirectory(buildDir);

        var cimguizmoCpp = Path.Combine(cimguizmoDir, CImguizmoBuildConstants.CIMGUIMO_CPP_FILE);
        var imguizmoCpp = Path.Combine(cimguizmoDir, CImguizmoBuildConstants.IMGUIZMO_DIR_NAME, CImguizmoBuildConstants.IMGUIZMO_CPP_FILE);
        var cimguiLib = FindCimguiImportLibrary(cimguiBuildDir, config);
        var outputLib = Path.Combine(buildDir, $"{CImguizmoBuildConstants.OUTPUT_DLL_NAME}.dll");

        var includes = new[]
        {
            cimguizmoDir,
            Path.Combine(cimguizmoDir, CImguizmoBuildConstants.IMGUIZMO_DIR_NAME),
            cimguiDir,
            Path.Combine(cimguiDir, "imgui"),
        };

        var includeArgs = string.Join(" ", includes.Select(path => $"/I\"{path}\""));
        var cflags = config == GlobalBuildConstants.DEBUG_CONFIG ? "/Od /Zi" : "/O2";
        var args = $"/LD {cflags} {includeArgs} \"{cimguizmoCpp}\" \"{imguizmoCpp}\" /link /OUT:\"{outputLib}\" \"{cimguiLib}\"";

        GlobalBuildUtils.Run("cl", args, cimguizmoDir);
    }

    private static string FindCimguiImportLibrary(string cimguiBuildDir, string config)
    {
        if (!Directory.Exists(cimguiBuildDir))
        {
            throw new DirectoryNotFoundException($"cimgui build directory not found: {cimguiBuildDir}");
        }

        var candidates = Directory.EnumerateFiles(cimguiBuildDir, "*.lib", SearchOption.AllDirectories)
            .Where(path => path.Contains("cimgui", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (candidates.Count == 0)
        {
            throw new FileNotFoundException($"cimgui import library not found under: {cimguiBuildDir}");
        }

        var configToken = config == GlobalBuildConstants.DEBUG_CONFIG ? "debug" : "release";
        var match = candidates.FirstOrDefault(path => path.Contains(configToken, StringComparison.OrdinalIgnoreCase));
        return match ?? candidates[0];
    }
}
