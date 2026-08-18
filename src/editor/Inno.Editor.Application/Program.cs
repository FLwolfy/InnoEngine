using System;
using System.IO;

namespace Inno.Editor.Application;

internal static class Program
{
    private const string TEMPORARY_PROJECT_DIRECTORY = "/Users/aaronliao/Dev/GameEngineDev/InnoProject";

    private static int Main(string[] args)
    {
        try
        {
            string projectDirectory = args.Length > 0 ? args[0] : Path.GetFullPath(TEMPORARY_PROJECT_DIRECTORY);
            using EditorHost host = new(projectDirectory);
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
}
