using System;
using System.IO;

using Xunit;

namespace Inno.Native.Bgfx.Tools.Tests;

public sealed class ToolRunnerTests
{
    [Fact]
    public void Run_EnsuresToolsCopiedToOutput()
    {
        foreach (var tool in Enum.GetValues<BgfxTool>())
        {
            var toolName = tool.ToString().ToLowerInvariant();
            ToolRunner.Run(tool, "--help");
            AssertToolExists(toolName);
        }
    }

    [Fact]
    public void Run_Throws_WhenToolMissing()
    {
        Assert.Throws<FileNotFoundException>(() => ToolRunner.Run((BgfxTool)999, "--help"));
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
        var platform = OperatingSystem.IsMacOS() ? "osx-arm64" : "windows-x64";
        var toolsDir = Path.Combine(AppContext.BaseDirectory, "native", "bgfx", platform, "tools");
        var suffix = GetConfigSuffix();
        var primaryName = $"{toolName}{suffix}";
        var primaryPath = Path.Combine(toolsDir, OperatingSystem.IsWindows() ? $"{primaryName}.exe" : primaryName);
        var fallbackPath = Path.Combine(toolsDir, OperatingSystem.IsWindows() ? $"{toolName}.exe" : toolName);

        Assert.True(
            File.Exists(primaryPath) || File.Exists(fallbackPath),
            $"Tool missing: {toolName} (expected: {primaryPath} or {fallbackPath})");
    }
}
