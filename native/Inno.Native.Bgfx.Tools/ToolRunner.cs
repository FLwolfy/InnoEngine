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
        var primaryFileName = OperatingSystem.IsWindows() ? $"{primaryName}.exe" : primaryName;
        var fallbackFileName = OperatingSystem.IsWindows() ? $"{toolName}.exe" : toolName;

        TryEnsureNativeFile(primaryFileName);
        TryEnsureNativeFile(fallbackFileName);

        if (TryFindNativeFile(primaryFileName, out var primaryPath))
        {
            return primaryPath;
        }

        if (TryFindNativeFile(fallbackFileName, out var fallbackPath))
        {
            return fallbackPath;
        }

        var fromRepo = TryResolveFromRepo(toolName);
        if (fromRepo is not null)
        {
            return fromRepo;
        }

        throw new FileNotFoundException(
            $"Unable to resolve bgfx tool '{toolName}'. Tried native output and extern/bgfx/tools/bin.");
    }

    private static string GetConfigSuffix()
    {
#if DEBUG
        return "-debug";
#else
        return "-release";
#endif
    }

    private static void TryEnsureNativeFile(string fileName)
    {
        try
        {
            NativeDllLoader.EnsureNativeFile(fileName);
        }
        catch (Exception)
        {
            // Ignore here and continue with other resolution paths.
        }
    }

    private static bool TryFindNativeFile(string fileName, out string fullPath)
    {
        try
        {
            fullPath = NativeDllLoader.FindNativeFile(fileName);
            return true;
        }
        catch (FileNotFoundException)
        {
            fullPath = string.Empty;
            return false;
        }
    }

    private static string? TryResolveFromRepo(string toolName)
    {
        var repoRoot = FindRepoRoot();
        if (repoRoot is null)
        {
            return null;
        }

        var platform = OperatingSystem.IsMacOS() ? "darwin" : OperatingSystem.IsWindows() ? "windows" : "linux";
        var fileName = OperatingSystem.IsWindows() ? $"{toolName}.exe" : toolName;
        var candidate = Path.Combine(repoRoot, "extern", "bgfx", "tools", "bin", platform, fileName);
        return File.Exists(candidate) ? candidate : null;
    }

    private static string? FindRepoRoot()
    {
        var starts = new[]
        {
            AppContext.BaseDirectory,
            Directory.GetCurrentDirectory()
        };

        foreach (var start in starts)
        {
            var dir = new DirectoryInfo(start);
            while (dir != null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "InnoEngine.sln")))
                {
                    return dir.FullName;
                }

                dir = dir.Parent;
            }
        }

        return null;
    }
}
