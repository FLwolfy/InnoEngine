using System;
using System.Diagnostics.CodeAnalysis;

namespace Inno.Editor.Application;

internal static class Program
{
    private static int Main(string[] args)
    {
        if (!TryGetRunOptions(args, out string? projectDirectory, out int? smokeFrameLimit))
        {
            Console.Error.WriteLine(
                "Usage: Inno.Editor.Application <project-directory> [--smoke-frames <positive-count>]");
            return 2;
        }
        try
        {
            using EditorHost host = EditorHost.Create(projectDirectory);
            int exitCode = host.Run(smokeFrameLimit);
            return exitCode;
        }
        catch (Exception ex)
        {
            string msg = $"[{DateTime.Now:O}] Unhandled exception:{Environment.NewLine}{ex}{Environment.NewLine}";
            Console.Error.WriteLine(msg);
            return 1;
        }
    }

    internal static bool TryGetProjectDirectory(
        string[] args,
        [NotNullWhen(true)] out string? projectDirectory)
    {
        ArgumentNullException.ThrowIfNull(args);
        projectDirectory = args.Length == 1 ? args[0] : null;
        return projectDirectory is not null;
    }

    private static bool TryGetRunOptions(
        string[] args,
        [NotNullWhen(true)] out string? projectDirectory,
        out int? smokeFrameLimit)
    {
        ArgumentNullException.ThrowIfNull(args);
        if (TryGetProjectDirectory(args, out projectDirectory))
        {
            smokeFrameLimit = null;
            return true;
        }

        smokeFrameLimit = null;
        projectDirectory = null;
        if (args.Length != 3 ||
            !string.Equals(args[1], "--smoke-frames", StringComparison.Ordinal) ||
            !int.TryParse(args[2], out int parsedFrameLimit) ||
            parsedFrameLimit <= 0)
        {
            return false;
        }

        projectDirectory = args[0];
        smokeFrameLimit = parsedFrameLimit;
        return true;
    }
}
