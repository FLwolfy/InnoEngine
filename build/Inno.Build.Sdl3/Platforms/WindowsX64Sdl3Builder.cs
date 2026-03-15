using System.IO;
using System.Runtime.InteropServices;
using Inno.Build.Global;

namespace Inno.Build.Sdl3.Platforms;

public sealed class WindowsX64Sdl3Builder : Sdl3Builder
{
    private const string OUTPUT_PLATFORM = "windows-x64";
    private const string BUILD_DIR_NAME = "windows-x64";
    private const string GENERATOR = "Visual Studio 17 2022";
    private const string PLATFORM = "x64";

    public override string OutputPlatform => OUTPUT_PLATFORM;

    public override bool IsSupported()
    {
        return RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            && RuntimeInformation.ProcessArchitecture == Architecture.X64;
    }

    public override void Build(string sdlDir, string config)
    {
        var buildDir = Path.Combine(sdlDir, Sdl3BuildConstants.BUILD_DIR_NAME, BUILD_DIR_NAME);
        var buildType = GetBuildType(config);

        GlobalBuildUtils.Run("cmake", $"-S . -B \"{buildDir}\" -G \"{GENERATOR}\" -A {PLATFORM} -DSDL_SHARED=ON -DSDL_STATIC=OFF", sdlDir);
        GlobalBuildUtils.Run("cmake", $"--build \"{buildDir}\" --config {buildType}", sdlDir);
    }
}
