using System;
using System.IO;

using Inno.Editor.Scripting;

namespace Inno.Editor.Application;

internal static class Program
{
    private static int Main(string[] args)
    {
        try
        {
            if (args.Length == 2 && string.Equals(args[0], "--generate-project", StringComparison.Ordinal))
            {
                using var scripts = new ScriptManager(new ScriptManagerOptions
                {
                    projectRootDirectory = Path.GetFullPath(args[1]),
                    autoCompile = false
                });
                scripts.GenerateProjectFiles();
                return 0;
            }
            using EditorHost host = new();
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
