using System;
using System.Diagnostics;
using System.IO;

namespace Inno.Build.Global;

public static class GlobalBuildUtils
{
    public static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, GlobalBuildConstants.REPO_ROOT_MARKER_FILE)))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("Could not locate repo root (InnoEngine.sln not found).");
    }

    public static string DefaultConfig()
    {
#if DEBUG
        return GlobalBuildConstants.DEBUG_CONFIG;
#else
        return GlobalBuildConstants.RELEASE_CONFIG;
#endif
    }

    public static void Run(string fileName, string arguments, string workingDir)
    {
        Console.WriteLine($"> {fileName} {arguments}");
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            WorkingDirectory = workingDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        using var process = Process.Start(psi);
        if (process == null)
        {
            throw new InvalidOperationException($"Failed to start process: {fileName}");
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

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"{fileName} exited with code {process.ExitCode}.");
        }
    }

    public static bool ContainsAny(string value, params string[] needles)
    {
        foreach (var needle in needles)
        {
            if (value.Contains(needle, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    public static string NormalizeOutputName(string fileName, string config)
    {
        var ext = Path.GetExtension(fileName);
        var baseName = Path.GetFileNameWithoutExtension(fileName);
        var trimmed = TrimConfigSuffix(baseName);
        return $"{trimmed}-{config}{ext}";
    }

    public static string TrimConfigSuffix(string baseName)
    {
        if (baseName.EndsWith("Release", StringComparison.OrdinalIgnoreCase))
        {
            baseName = baseName[..^"Release".Length];
        }
        else if (baseName.EndsWith("Debug", StringComparison.OrdinalIgnoreCase))
        {
            baseName = baseName[..^"Debug".Length];
        }

        return baseName.TrimEnd('-', '_', '.');
    }
}
