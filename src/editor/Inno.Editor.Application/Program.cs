using System;
using System.Diagnostics.CodeAnalysis;

namespace Inno.Editor.Application;

internal static class Program
{
    private static int Main(string[] args)
    {
        if (!TryGetProjectDirectory(args, out string? projectDirectory))
        {
            Console.Error.WriteLine("Usage: Inno.Editor.Application <project-directory>");
            return 2;
        }
        try
        {
            using EditorHost host = EditorHost.Create(projectDirectory);
            int exitCode = host.Run();
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
}
