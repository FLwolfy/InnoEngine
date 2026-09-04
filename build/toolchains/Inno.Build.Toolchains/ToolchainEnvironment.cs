using System;
using System.Diagnostics;
using System.IO;

namespace Inno.Build.Toolchains;

/// <summary>
/// Provides deterministic host-process and workspace operations shared by native dependency toolchains.
/// </summary>
public static class ToolchainEnvironment
{
    /// <summary>
    /// Resolves the repository containing the currently executing toolchain assembly.
    /// </summary>
    /// <returns>
    /// The absolute path of the repository root.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when no repository marker exists above the toolchain assembly directory.
    /// </exception>
    public static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, ToolchainLayout.C_REPOSITORY_MARKER_FILE)))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("Could not locate repo root (InnoEngine.sln not found).");
    }

    /// <summary>
    /// Resolves the native configuration corresponding to the current managed build configuration.
    /// </summary>
    /// <returns>
    /// The normalized debug or release configuration token.
    /// </returns>
    public static string DefaultConfig()
    {
#if DEBUG
        return ToolchainLayout.C_DEBUG_CONFIGURATION;
#else
        return ToolchainLayout.C_RELEASE_CONFIGURATION;
#endif
    }

    /// <summary>
    /// Runs one native build process synchronously while forwarding its output streams.
    /// </summary>
    /// <param name="fileName">
    /// The executable resolved by the host operating system.
    /// </param>
    /// <param name="arguments">
    /// The complete command-line argument string accepted by the executable.
    /// </param>
    /// <param name="workingDir">
    /// The absolute working directory assigned to the child process.
    /// </param>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the process cannot start or exits with a nonzero code.
    /// </exception>
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

    /// <summary>
    /// Determines whether a value contains at least one token without regard to casing.
    /// </summary>
    /// <param name="value">
    /// The value searched for candidate tokens.
    /// </param>
    /// <param name="needles">
    /// The candidate tokens tested against the value.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when at least one token occurs in the value; otherwise <see langword="false"/>.
    /// </returns>
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

    /// <summary>
    /// Normalizes a native artifact name and appends its build configuration.
    /// </summary>
    /// <param name="fileName">
    /// The source artifact file name.
    /// </param>
    /// <param name="config">
    /// The normalized configuration appended to the artifact stem.
    /// </param>
    /// <returns>
    /// The deterministic output file name.
    /// </returns>
    public static string NormalizeOutputName(string fileName, string config)
    {
        var ext = Path.GetExtension(fileName);
        var baseName = Path.GetFileNameWithoutExtension(fileName);
        var trimmed = TrimConfigSuffix(baseName);
        return $"{trimmed}-{config}{ext}";
    }

    /// <summary>
    /// Removes a trailing native debug or release token from an artifact stem.
    /// </summary>
    /// <param name="baseName">
    /// The artifact stem to normalize.
    /// </param>
    /// <returns>
    /// The normalized artifact stem without a configuration suffix.
    /// </returns>
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

    /// <summary>
    /// Deletes one explicitly resolved toolchain output directory when it exists.
    /// </summary>
    /// <param name="path">
    /// The exact directory selected by a toolchain command.
    /// </param>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="path"/> is empty or does not resolve to an absolute path.
    /// </exception>
    public static void DeleteDirectory(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!Path.IsPathFullyQualified(path))
        {
            throw new ArgumentException("Toolchain deletion requires an absolute path.", nameof(path));
        }

        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }
}
