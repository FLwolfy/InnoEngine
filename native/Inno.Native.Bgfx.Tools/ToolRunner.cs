using System;
using System.Diagnostics;
using System.IO;
using Inno.Native.Dll;

namespace Inno.Native.Bgfx.Tools;

public static class ToolRunner
{
    public static int Run(string toolName, string arguments, string? workingDirectory = null)
    {
        var toolPath = ResolveToolPath(toolName);
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

    public static string ResolveToolPath(string toolName)
    {
        var fileName = OperatingSystem.IsWindows() ? $"{toolName}.exe" : toolName;
        return NativeDllLoader.FindNativeFile(fileName);
    }
}
