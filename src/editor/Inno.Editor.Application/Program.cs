using System;

namespace Inno.Editor.Application;

internal static class Program
{
    private static int Main()
    {
        try
        {
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
