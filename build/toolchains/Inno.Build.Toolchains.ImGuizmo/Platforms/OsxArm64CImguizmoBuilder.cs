using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Inno.Build.Toolchains;

namespace Inno.Build.Toolchains.ImGuizmo.Platforms;

internal sealed class OsxArm64CImguizmoBuilder : CImguizmoBuilder
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
    /// <param name="cimguizmoDir">
    /// The cimguizmo dir text validated by the build operation.
    /// </param>
    /// <param name="cimguiDir">
    /// The cimgui dir text validated by the build operation.
    /// </param>
    /// <param name="cimguiBuildDir">
    /// The cimgui build dir text validated by the build operation.
    /// </param>
    /// <param name="cimguiOutputDir">
    /// The cimgui output dir text validated by the build operation.
    /// </param>
    /// <param name="config">
    /// The validated configuration that controls this operation.
    /// </param>
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
        var cflags = config == ToolchainLayout.C_DEBUG_CONFIGURATION ? "-O0 -g" : "-O3";
        var rpath = "@loader_path/../cimgui/osx-arm64";
        var installName = $"@rpath/{CImguizmoBuildConstants.OUTPUT_DLL_NAME}.dylib";
        var args = $"{cflags} -std=c++11 -fPIC -dynamiclib {includeArgs} \"{cimguizmoCpp}\" \"{imguizmoCpp}\" \"{cimguiLib}\" -Wl,-install_name,{installName} -Wl,-rpath,{rpath} -o \"{outputLib}\"";

        ToolchainEnvironment.Run("clang++", args, cimguizmoDir);
    }

    private static string FindCimguiLibrary(string cimguiOutputDir, string config)
    {
        var name = config == ToolchainLayout.C_DEBUG_CONFIGURATION
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
