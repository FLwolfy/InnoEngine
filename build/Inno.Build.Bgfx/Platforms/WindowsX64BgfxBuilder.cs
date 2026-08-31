using System.Runtime.InteropServices;
using Inno.Build.Global;

namespace Inno.Build.Bgfx.Platforms;

public sealed class WindowsX64BgfxBuilder : BgfxBuilder
{
    public const string OUTPUT_PLATFORM = "windows-x64";
    private const string DEBUG_TARGET = "vs2022-debug64";
    private const string RELEASE_TARGET = "vs2022-release64";
    private const string GENIE_RELATIVE_PATH = @"..\bx\tools\bin\windows\genie";
    private const string VS2022_SOLUTION_RELATIVE_PATH = @".build\projects\vs2022\bgfx.sln";
    private const string PLATFORM = "x64";

    public override string outputPlatform => OUTPUT_PLATFORM;
    protected override string debugMakeTarget => DEBUG_TARGET;
    protected override string releaseMakeTarget => RELEASE_TARGET;

    public override bool IsSupported()
    {
        return RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            && RuntimeInformation.ProcessArchitecture == Architecture.X64;
    }

    public override void Build(string bgfxDir, string config, string? makeTargetOverride)
    {
        if (!string.IsNullOrWhiteSpace(makeTargetOverride))
        {
            GlobalBuildUtils.Run("make", makeTargetOverride, bgfxDir);
            return;
        }

        RunGenie(bgfxDir, "--with-shared-lib");
        RunMsBuild(bgfxDir, config);
    }

    public override void BuildTools(string bgfxDir, string config)
    {
        RunGenie(bgfxDir, "--with-tools --with-shared-lib");
        RunMsBuild(bgfxDir, config);
    }

    private static void RunGenie(string bgfxDir, string args)
    {
        GlobalBuildUtils.Run(GENIE_RELATIVE_PATH, $"{args} vs2022", bgfxDir);
    }

    private static void RunMsBuild(string bgfxDir, string config)
    {
        var vsConfig = config == GlobalBuildConstants.DEBUG_CONFIG ? "Debug" : "Release";
        var args = $"{VS2022_SOLUTION_RELATIVE_PATH} /m /p:Configuration={vsConfig} /p:Platform={PLATFORM}";
        GlobalBuildUtils.Run("msbuild", args, bgfxDir);
    }
}
