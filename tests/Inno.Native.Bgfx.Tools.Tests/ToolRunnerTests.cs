using System;
using System.IO;

using Xunit;

namespace Inno.Native.Bgfx.Tools.Tests;

public sealed class ToolRunnerTests
{
    [Theory]
    [InlineData("shaderc")]
    [InlineData("geometryc")]
    [InlineData("geometryv")]
    [InlineData("texturec")]
    [InlineData("texturev")]
    public void ResolveToolPath_FindsToolsFromOutput(string toolName)
    {
        var resolved = ToolRunner.ResolveToolPath(toolName);
        var platform = OperatingSystem.IsMacOS() ? "osx-arm64" : "windows-x64";
        var toolsDir = Path.Combine(AppContext.BaseDirectory, "native", "bgfx", platform, "tools");
        Assert.StartsWith(toolsDir, resolved, StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(resolved), $"Tool missing: {toolName} (resolved: {resolved})");
    }

    [Theory]
    [InlineData("shaderc")]
    [InlineData("geometryc")]
    [InlineData("geometryv")]
    [InlineData("texturec")]
    [InlineData("texturev")]
    public void ResolveToolPath_Throws_WhenOutputMissing(string toolName)
    {
        var toolsRoot = Path.Combine(AppContext.BaseDirectory, "native");
        if (Directory.Exists(toolsRoot))
        {
            Directory.Delete(toolsRoot, recursive: true);
        }

        Assert.Throws<FileNotFoundException>(() => ToolRunner.ResolveToolPath(toolName));
    }
}
