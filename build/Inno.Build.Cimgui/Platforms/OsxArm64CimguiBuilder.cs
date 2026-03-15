using System.IO;
using System.Runtime.InteropServices;
using Inno.Build.Global;

namespace Inno.Build.Cimgui.Platforms;

public sealed class OsxArm64CimguiBuilder : CimguiBuilder
{
    private const string OUTPUT_PLATFORM = "osx-arm64";
    private const string BUILD_DIR_NAME = "osx-arm64";

    public override string outputPlatform => OUTPUT_PLATFORM;

    public override bool IsSupported()
    {
        return RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
            && RuntimeInformation.ProcessArchitecture == Architecture.Arm64;
    }

    public override void Build(string cimguiDir, string config)
    {
        var buildDir = Path.Combine(cimguiDir, CimguiBuildConstants.BUILD_DIR_NAME, BUILD_DIR_NAME);
        var buildType = GetBuildType(config);

        GlobalBuildUtils.Run("cmake", $"-S . -B \"{buildDir}\" -DCMAKE_BUILD_TYPE={buildType} -DBUILD_SHARED_LIBS=ON", cimguiDir);
        GlobalBuildUtils.Run("cmake", $"--build \"{buildDir}\" --config {buildType}", cimguiDir);
    }
}
