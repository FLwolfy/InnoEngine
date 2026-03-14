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
    public void ResolveToolPath_UsesOverrideDirectory(string toolName)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "inno-bgfx-tools-test", Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(tempDir);

        var toolFile = OperatingSystem.IsWindows() ? $"{toolName}.exe" : toolName;
        var toolPath = Path.Combine(tempDir, toolFile);
        File.WriteAllText(toolPath, string.Empty);

        Environment.SetEnvironmentVariable("INNO_BGFX_TOOLS_DIR", tempDir);
        try
        {
            var resolved = ToolRunner.ResolveToolPath(toolName);
            Assert.Equal(toolPath, resolved);
        }
        finally
        {
            Environment.SetEnvironmentVariable("INNO_BGFX_TOOLS_DIR", null);
            Directory.Delete(tempDir, recursive: true);
        }
    }
}
