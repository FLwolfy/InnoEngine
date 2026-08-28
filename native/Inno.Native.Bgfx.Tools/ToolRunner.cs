using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Inno.Native.Dll;

namespace Inno.Native.Bgfx.Tools;

/// <summary>
/// Contains the immutable result of one bgfx tool invocation.
/// </summary>
public sealed class ToolRunResult
{
    /// <summary>
    /// Creates a tool invocation result.
    /// </summary>
    /// <param name="exitCode">Native process exit code.</param>
    /// <param name="standardOutput">Captured standard output.</param>
    /// <param name="standardError">Captured standard error.</param>
    public ToolRunResult(int exitCode, string standardOutput, string standardError)
    {
        this.exitCode = exitCode;
        this.standardOutput = standardOutput ?? string.Empty;
        this.standardError = standardError ?? string.Empty;
    }

    /// <summary>Gets the native process exit code.</summary>
    public int exitCode { get; }

    /// <summary>Gets captured standard output.</summary>
    public string standardOutput { get; }

    /// <summary>Gets captured standard error.</summary>
    public string standardError { get; }

    /// <summary>Gets whether the tool exited successfully.</summary>
    public bool succeeded => exitCode == 0;
}

/// <summary>
/// Runs bgfx tool executables from the native output with argument-safe process invocation.
/// </summary>
public static class ToolRunner
{
    /// <summary>
    /// Runs the specified tool and captures its output.
    /// </summary>
    /// <param name="tool">Tool to execute.</param>
    /// <param name="arguments">Individual command-line arguments without shell quoting.</param>
    /// <param name="workingDirectory">Optional working directory; defaults to <see cref="AppContext.BaseDirectory"/>.</param>
    /// <returns>The exit code and captured output.</returns>
    public static ToolRunResult Run(
        BgfxTool tool,
        IReadOnlyList<string> arguments,
        string? workingDirectory = null)
        => RunAsync(tool, arguments, workingDirectory).AsTask().GetAwaiter().GetResult();

    /// <summary>
    /// Runs the specified tool asynchronously and captures its output.
    /// </summary>
    /// <param name="tool">Tool to execute.</param>
    /// <param name="arguments">Individual command-line arguments without shell quoting.</param>
    /// <param name="workingDirectory">Optional working directory; defaults to <see cref="AppContext.BaseDirectory"/>.</param>
    /// <param name="cancellationToken">Cancellation that terminates the child process tree.</param>
    /// <returns>The exit code and captured output.</returns>
    /// <exception cref="FileNotFoundException">Thrown when the requested bgfx tool cannot be resolved.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the native process cannot be started.</exception>
    public static async ValueTask<ToolRunResult> RunAsync(
        BgfxTool tool,
        IReadOnlyList<string> arguments,
        string? workingDirectory = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        string toolPath = ResolveToolPath(tool);
        var startInfo = new ProcessStartInfo
        {
            FileName = toolPath,
            WorkingDirectory = workingDirectory ?? AppContext.BaseDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (string argument in arguments)
        {
            ArgumentNullException.ThrowIfNull(argument);
            startInfo.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Failed to start tool: {toolPath}");
        try
        {
            Task<string> standardOutput = process.StandardOutput.ReadToEndAsync(cancellationToken);
            Task<string> standardError = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            return new ToolRunResult(
                process.ExitCode,
                await standardOutput.ConfigureAwait(false),
                await standardError.ConfigureAwait(false));
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            throw;
        }
    }

    private static string ResolveToolPath(BgfxTool tool)
    {
        string toolName = tool.ToString().ToLowerInvariant();
        string suffix = GetConfigSuffix();
        string primaryName = $"{toolName}{suffix}";
        string primaryFileName = OperatingSystem.IsWindows() ? $"{primaryName}.exe" : primaryName;
        string fallbackFileName = OperatingSystem.IsWindows() ? $"{toolName}.exe" : toolName;

        TryEnsureNativeFile(primaryFileName);
        TryEnsureNativeFile(fallbackFileName);

        if (TryFindNativeFile(primaryFileName, out string primaryPath))
        {
            return primaryPath;
        }

        if (TryFindNativeFile(fallbackFileName, out string fallbackPath))
        {
            return fallbackPath;
        }

        string? fromRepo = TryResolveFromRepo(toolName);
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
            // Resolution continues through the local native output and repository fallback.
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
        string? repoRoot = FindRepoRoot();
        if (repoRoot is null)
        {
            return null;
        }

        string platform = OperatingSystem.IsMacOS()
            ? "darwin"
            : OperatingSystem.IsWindows()
                ? "windows"
                : "linux";
        string fileName = OperatingSystem.IsWindows() ? $"{toolName}.exe" : toolName;
        string candidate = Path.Combine(repoRoot, "extern", "bgfx", "tools", "bin", platform, fileName);
        return File.Exists(candidate) ? candidate : null;
    }

    private static string? FindRepoRoot()
    {
        string[] starts =
        [
            AppContext.BaseDirectory,
            Directory.GetCurrentDirectory()
        ];

        foreach (string start in starts)
        {
            for (DirectoryInfo? directory = new(start); directory is not null; directory = directory.Parent)
            {
                if (File.Exists(Path.Combine(directory.FullName, "InnoEngine.sln")))
                {
                    return directory.FullName;
                }
            }
        }

        return null;
    }
}
