using System.Runtime.InteropServices;
using Inno.Build.Global;

namespace Inno.Build.Bgfx.Platforms;

public sealed class OsxArm64BgfxBuilder : BgfxBuilder
{
    public const string OUTPUT_PLATFORM = "osx-arm64";
    private const string DEBUG_TARGET = "osx-arm64-debug";
    private const string RELEASE_TARGET = "osx-arm64-release";

    public override string outputPlatform => OUTPUT_PLATFORM;
    protected override string debugMakeTarget => DEBUG_TARGET;
    protected override string releaseMakeTarget => RELEASE_TARGET;

    public override bool IsSupported()
    {
        return RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
            && RuntimeInformation.ProcessArchitecture == Architecture.Arm64;
    }

    public override void Build(string bgfxDir, string config, string? makeTargetOverride)
    {
        if (!string.IsNullOrWhiteSpace(makeTargetOverride))
        {
            GlobalBuildUtils.Run("make", makeTargetOverride, bgfxDir);
            return;
        }

        var target = GetMakeTarget(config);
        GlobalBuildUtils.Run("make", target, bgfxDir);
    }

    public override void BuildTools(string bgfxDir, string config)
    {
        GlobalBuildUtils.Run("make", $"tools config={config}", bgfxDir);
    }
}
