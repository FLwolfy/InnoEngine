using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace Inno.Build.Bgfx.Common;

public static class BgfxBuildUtils
{
    public static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, BgfxBuildConstants.REPO_ROOT_MARKER_FILE)))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("Could not locate repo root (InnoEngine.sln not found).");
    }

    public static void ValidateSubmodules(string bgfxDir, string bxDir, string bimgDir)
    {
        if (!Directory.Exists(bgfxDir))
        {
            throw new DirectoryNotFoundException(
                $"bgfx submodule not found at {bgfxDir}. Please initialize submodules before running this tool.");
        }

        if (!Directory.Exists(bxDir) || !Directory.Exists(bimgDir))
        {
            throw new DirectoryNotFoundException(
                $"bx/bimg submodules not found next to bgfx. Expected {bxDir} and {bimgDir}.");
        }
    }

    public static (string OutputPlatform, string MakeTarget) DetectDefaults(string config)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            if (RuntimeInformation.ProcessArchitecture != Architecture.Arm64)
            {
                throw new PlatformNotSupportedException("Only macos-arm64 is supported. Use --make-target to override.");
            }

            var outputPlatform = BgfxBuildConstants.OSX_ARM64_PLATFORM;
            var makeTarget = config == BgfxBuildConstants.DEBUG_CONFIG
                ? BgfxBuildConstants.OSX_ARM64_DEBUG_TARGET
                : BgfxBuildConstants.OSX_ARM64_RELEASE_TARGET;
            return (outputPlatform, makeTarget);
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            if (RuntimeInformation.ProcessArchitecture != Architecture.X64)
            {
                throw new PlatformNotSupportedException("Only windows-x64 is supported. Use --make-target to override.");
            }

            var outputPlatform = BgfxBuildConstants.WINDOWS_X64_PLATFORM;
            var makeTarget = config == BgfxBuildConstants.DEBUG_CONFIG
                ? BgfxBuildConstants.VS2022_DEBUG_TARGET
                : BgfxBuildConstants.VS2022_RELEASE_TARGET;
            return (outputPlatform, makeTarget);
        }

        throw new PlatformNotSupportedException("Only macOS and Windows are supported by default. Use --make-target to override.");
    }

    public static string DefaultConfig()
    {
#if DEBUG
        return BgfxBuildConstants.DEBUG_CONFIG;
#else
        return BgfxBuildConstants.RELEASE_CONFIG;
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
}
