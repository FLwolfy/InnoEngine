using System.Runtime.InteropServices;
using Inno.Build.Toolchains;

namespace Inno.Build.Toolchains.Bgfx.Platforms;

internal sealed class OsxArm64BgfxBuilder : BgfxBuilder
{
    /// <summary>
    /// The output platform value used as part of this type's public representation.
    /// </summary>
    public const string OUTPUT_PLATFORM = "osx-arm64";
    private const string DEBUG_TARGET = "osx-arm64-debug";
    private const string RELEASE_TARGET = "osx-arm64-release";

    /// <summary>
    /// Gets the native platform identifier produced by this builder.
    /// </summary>
    public override string outputPlatform => OUTPUT_PLATFORM;
    /// <summary>
    /// Gets the native make target used for debug output.
    /// </summary>
    protected override string debugMakeTarget => DEBUG_TARGET;
    /// <summary>
    /// Gets the native make target used for optimized output.
    /// </summary>
    protected override string releaseMakeTarget => RELEASE_TARGET;

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
    /// <param name="bgfxDir">
    /// The bgfx dir text validated by the build operation.
    /// </param>
    /// <param name="config">
    /// The validated configuration that controls this operation.
    /// </param>
    /// <param name="makeTargetOverride">
    /// The make target override text validated by the build operation.
    /// </param>
    public override void Build(string bgfxDir, string config, string? makeTargetOverride)
    {
        if (!string.IsNullOrWhiteSpace(makeTargetOverride))
        {
            ToolchainEnvironment.Run("make", makeTargetOverride, bgfxDir);
            return;
        }

        var target = GetMakeTarget(config);
        ToolchainEnvironment.Run("make", target, bgfxDir);
    }

    /// <summary>
    /// Builds the native offline tools required by the selected configuration.
    /// </summary>
    /// <param name="bgfxDir">
    /// The bgfx dir text validated by the build tools operation.
    /// </param>
    /// <param name="config">
    /// The validated configuration that controls this operation.
    /// </param>
    public override void BuildTools(string bgfxDir, string config)
    {
        ToolchainEnvironment.Run("make", $"tools config={config}", bgfxDir);
    }
}
