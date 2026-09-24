using System;
using System.IO;
using System.Runtime.InteropServices;
using Inno.Build.Toolchains;

namespace Inno.Build.Toolchains.Bgfx.Platforms;

internal sealed class LinuxBgfxBuilder : BgfxBuilder
{
    private const string LINUX_X64_OUTPUT_PLATFORM = "linux-x64";
    private const string LINUX_ARM64_OUTPUT_PLATFORM = "linux-arm64";
    private const string LINUX_X64_DEBUG_TARGET = "linux-gcc-debug64";
    private const string LINUX_X64_RELEASE_TARGET = "linux-gcc-release64";
    private const string LINUX_ARM_GCC_PROJECT = ".build/projects/gmake-linux-arm-gcc";
    private const string HOST_GENIE_ENVIRONMENT_VARIABLE = "BGFX_GENIE";

    private static string ParallelMakeOption => $"-j{Math.Max(1, Environment.ProcessorCount)}";

    public override string outputPlatform => RuntimeInformation.ProcessArchitecture switch
    {
        Architecture.X64 => LINUX_X64_OUTPUT_PLATFORM,
        Architecture.Arm64 => LINUX_ARM64_OUTPUT_PLATFORM,
        _ => throw new PlatformNotSupportedException(
            $"Unsupported Linux architecture: {RuntimeInformation.ProcessArchitecture}.")
    };

    public override string artifactPathToken => RuntimeInformation.ProcessArchitecture switch
    {
        Architecture.X64 => "/linux64_gcc/bin/",
        Architecture.Arm64 => "/linux32_arm_gcc/bin/",
        _ => throw new PlatformNotSupportedException(
            $"Unsupported Linux architecture: {RuntimeInformation.ProcessArchitecture}.")
    };

    protected override string debugMakeTarget => LINUX_X64_DEBUG_TARGET;

    protected override string releaseMakeTarget => LINUX_X64_RELEASE_TARGET;

    public override bool IsSupported()
    {
        return RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
            && RuntimeInformation.ProcessArchitecture is Architecture.X64 or Architecture.Arm64;
    }

    public override void Build(string bgfxDir, string config, string? makeTargetOverride)
    {
        if (!string.IsNullOrWhiteSpace(makeTargetOverride))
        {
            ToolchainEnvironment.Run("make", $"{ParallelMakeOption} {makeTargetOverride}", bgfxDir);
            return;
        }

        if (RuntimeInformation.ProcessArchitecture == Architecture.X64)
        {
            ToolchainEnvironment.Run("make", $"{ParallelMakeOption} {GetMakeTarget(config)}", bgfxDir);
            return;
        }

        GenerateArmProjects(bgfxDir, includeTools: false);
        ToolchainEnvironment.Run(
            "make",
            $"{ParallelMakeOption} -R -C {LINUX_ARM_GCC_PROJECT} config={config}",
            bgfxDir);
    }

    public override void BuildTools(string bgfxDir, string config)
    {
        if (RuntimeInformation.ProcessArchitecture == Architecture.X64)
        {
            ToolchainEnvironment.Run("make", $"{ParallelMakeOption} tools config={config}", bgfxDir);
            return;
        }

        GenerateArmProjects(bgfxDir, includeTools: true);
        ToolchainEnvironment.Run(
            "make",
            $"{ParallelMakeOption} -R -C {LINUX_ARM_GCC_PROJECT} "
                + $"geometryc geometryv shaderc texturec texturev config={config}",
            bgfxDir);
    }

    private static void GenerateArmProjects(string bgfxDir, bool includeTools)
    {
        var genie = ResolveHostGenie(bgfxDir);
        var toolsOption = includeTools ? "--with-tools " : string.Empty;
        ToolchainEnvironment.Run(
            genie,
            $"{toolsOption}--with-shared-lib --gcc=linux-arm-gcc gmake",
            bgfxDir);
    }

    private static string ResolveHostGenie(string bgfxDir)
    {
        var configured = Environment.GetEnvironmentVariable(HOST_GENIE_ENVIRONMENT_VARIABLE);
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }

        var repoRoot = Path.GetFullPath(Path.Combine(bgfxDir, "..", ".."));
        var localHostBuild = Path.Combine(repoRoot, "artifacts", "genie-src", "bin", "linux", "genie");
        if (File.Exists(localHostBuild))
        {
            return localHostBuild;
        }

        if (RuntimeInformation.ProcessArchitecture == Architecture.X64)
        {
            var bundled = Path.GetFullPath(Path.Combine(bgfxDir, "..", "bx", "tools", "bin", "linux", "genie"));
            if (File.Exists(bundled))
            {
                return bundled;
            }
        }

        var pathCandidate = FindOnPath("genie");
        if (pathCandidate != null)
        {
            return pathCandidate;
        }

        throw new FileNotFoundException(
            "A host-native GENie executable is required to generate bgfx projects. "
            + $"Set {HOST_GENIE_ENVIRONMENT_VARIABLE} to its absolute path. "
            + "The bgfx/bx bundled Linux binary only supports x86_64.");
    }

    private static string? FindOnPath(string executableName)
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(directory, executableName);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }
}
