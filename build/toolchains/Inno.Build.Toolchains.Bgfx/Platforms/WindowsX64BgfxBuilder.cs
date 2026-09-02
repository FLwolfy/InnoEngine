using System.Runtime.InteropServices;
using Inno.Build.Toolchains;

namespace Inno.Build.Toolchains.Bgfx.Platforms;

internal sealed class WindowsX64BgfxBuilder : BgfxBuilder
{
    /// <summary>
    /// The output platform value used as part of this type's public representation.
    /// </summary>
    public const string OUTPUT_PLATFORM = "windows-x64";
    private const string DEBUG_TARGET = "vs2022-debug64";
    private const string RELEASE_TARGET = "vs2022-release64";
    private const string GENIE_RELATIVE_PATH = @"..\bx\tools\bin\windows\genie";
    private const string VS2022_SOLUTION_RELATIVE_PATH = @".build\projects\vs2022\bgfx.sln";
    private const string PLATFORM = "x64";

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
        return RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            && RuntimeInformation.ProcessArchitecture == Architecture.X64;
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

        RunGenie(bgfxDir, "--with-shared-lib");
        RunMsBuild(bgfxDir, config);
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
        RunGenie(bgfxDir, "--with-tools --with-shared-lib");
        RunMsBuild(bgfxDir, config);
    }

    private static void RunGenie(string bgfxDir, string args)
    {
        ToolchainEnvironment.Run(GENIE_RELATIVE_PATH, $"{args} vs2022", bgfxDir);
    }

    private static void RunMsBuild(string bgfxDir, string config)
    {
        var vsConfig = config == ToolchainLayout.C_DEBUG_CONFIGURATION ? "Debug" : "Release";
        var args = $"{VS2022_SOLUTION_RELATIVE_PATH} /m /p:Configuration={vsConfig} /p:Platform={PLATFORM}";
        ToolchainEnvironment.Run("msbuild", args, bgfxDir);
    }
}
