using System;
using System.IO;
using System.Runtime.InteropServices;

using Xunit;

namespace Inno.Native.Bgfx.Tests;

public sealed class ToolRunnerTests
{
    [Fact]
    public void Run_EnsuresToolsCopiedToOutput()
    {
        foreach (var tool in Enum.GetValues<BgfxTool>())
        {
            var toolName = tool.ToString().ToLowerInvariant();
            ToolRunResult result = ToolRunner.Run(tool, ["--help"]);
            Assert.InRange(result.exitCode, 0, 1);
            Assert.False(string.IsNullOrWhiteSpace(result.standardOutput + result.standardError));
            AssertToolExists(toolName);
        }
    }

    [Fact]
    public void Run_Throws_WhenToolMissing()
    {
        Assert.Throws<FileNotFoundException>(() => ToolRunner.Run((BgfxTool)999, ["--help"]));
    }

    private static string GetConfigSuffix()
    {
#if DEBUG
        return "-debug";
#else
        return "-release";
#endif
    }

    private static void AssertToolExists(string toolName)
    {
        var platform = GetPlatformIdentifier();
        var toolsDir = Path.Combine(AppContext.BaseDirectory, "native", "bgfx", platform, "tools");
        var suffix = GetConfigSuffix();
        var primaryName = $"{toolName}{suffix}";
        var primaryPath = Path.Combine(toolsDir, OperatingSystem.IsWindows() ? $"{primaryName}.exe" : primaryName);
        var fallbackPath = Path.Combine(toolsDir, OperatingSystem.IsWindows() ? $"{toolName}.exe" : toolName);

        Assert.True(
            File.Exists(primaryPath) || File.Exists(fallbackPath),
            $"Tool missing: {toolName} (expected: {primaryPath} or {fallbackPath})");
    }

    private static string GetPlatformIdentifier()
    {
        string operatingSystem = OperatingSystem.IsMacOS()
            ? "osx"
            : OperatingSystem.IsWindows()
                ? "windows"
                : OperatingSystem.IsLinux()
                    ? "linux"
                    : throw new PlatformNotSupportedException();
        string architecture = RuntimeInformation.OSArchitecture switch
        {
            Architecture.X64 => "x64",
            Architecture.Arm64 => "arm64",
            Architecture.X86 => "x86",
            Architecture.Arm => "arm",
            _ => throw new PlatformNotSupportedException()
        };
        return $"{operatingSystem}-{architecture}";
    }
}
