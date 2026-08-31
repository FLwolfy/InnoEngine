using System.IO;
using System.Runtime.InteropServices;
using Inno.Build.Global;

namespace Inno.Build.Cimgui.Platforms;

public sealed class WindowsX64CimguiBuilder : CimguiBuilder
{
    private const string OUTPUT_PLATFORM = "windows-x64";
    private const string BUILD_DIR_NAME = "windows-x64";
    private const string GENERATOR = "Visual Studio 17 2022";
    private const string PLATFORM = "x64";

    public override string outputPlatform => OUTPUT_PLATFORM;

    public override bool IsSupported()
    {
        return RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            && RuntimeInformation.ProcessArchitecture == Architecture.X64;
    }

    public override void Build(string cimguiDir, string config)
    {
        var buildDir = Path.Combine(cimguiDir, CimguiBuildConstants.BUILD_DIR_NAME, BUILD_DIR_NAME);
        var buildType = GetBuildType(config);

        GlobalBuildUtils.Run("cmake", $"-S . -B \"{buildDir}\" -G \"{GENERATOR}\" -A {PLATFORM} -DBUILD_SHARED_LIBS=ON", cimguiDir);
        GlobalBuildUtils.Run("cmake", $"--build \"{buildDir}\" --config {buildType}", cimguiDir);
    }
}
