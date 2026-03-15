using System;
using System.Diagnostics;
using System.IO;
using Inno.Native.Dll;

namespace Inno.Native.Bgfx.Tools;

/// <summary>
/// Runs bgfx tool executables from the native output.
/// </summary>
public static class ToolRunner
{
    static ToolRunner()
    {
        var suffix = GetConfigSuffix();
        var tools = Enum.GetValues<BgfxTool>();
        foreach (var tool in tools)
        {
            var toolName = tool.ToString().ToLowerInvariant();
            var primaryName = $"{toolName}{suffix}";
            var primaryFileName = OperatingSystem.IsWindows() ? $"{primaryName}.exe" : primaryName;
            var fallbackFileName = OperatingSystem.IsWindows() ? $"{toolName}.exe" : toolName;

            try
            {
                NativeDllLoader.EnsureNativeFile(primaryFileName);
            }
            catch (FileNotFoundException)
            {
                NativeDllLoader.EnsureNativeFile(fallbackFileName);
            }
        }
    }

    /// <summary>
    /// Runs the specified tool with arguments.
    /// </summary>
    /// <param name="tool">Tool to execute.</param>
    /// <param name="arguments">Command line arguments.</param>
    /// <param name="workingDirectory">Optional working directory; defaults to AppContext.BaseDirectory.</param>
    /// <returns>Process exit code.</returns>
    public static int Run(BgfxTool tool, string arguments, string? workingDirectory = null)
    {
        var toolPath = ResolveToolPath(tool);
        var psi = new ProcessStartInfo
        {
            FileName = toolPath,
            Arguments = arguments,
            WorkingDirectory = workingDirectory ?? AppContext.BaseDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        using var process = Process.Start(psi);
        if (process == null)
        {
            throw new InvalidOperationException($"Failed to start tool: {toolPath}");
        }

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data != null)
            {
                Console.WriteLine(e.Data);
            }
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data != null)
            {
                Console.Error.WriteLine(e.Data);
            }
        };

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        process.WaitForExit();
        return process.ExitCode;
    }

    private static string ResolveToolPath(BgfxTool tool)
    {
        var toolName = tool.ToString().ToLowerInvariant();
        var suffix = GetConfigSuffix();
        var primaryName = $"{toolName}{suffix}";
        var fallbackName = toolName;

        var primaryFileName = OperatingSystem.IsWindows() ? $"{primaryName}.exe" : primaryName;
        var fallbackFileName = OperatingSystem.IsWindows() ? $"{fallbackName}.exe" : fallbackName;

        try
        {
            return NativeDllLoader.FindNativeFile(primaryFileName);
        }
        catch (FileNotFoundException)
        {
            return NativeDllLoader.FindNativeFile(fallbackFileName);
        }
    }

    private static string GetConfigSuffix()
    {
#if DEBUG
        return "-debug";
#else
        return "-release";
#endif
    }
}
