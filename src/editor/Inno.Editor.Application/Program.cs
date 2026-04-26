using System;
using System.IO;

namespace Inno.Editor.Application;

internal static class Program
{
    private static readonly string BOOT_LOG_PATH = Path.Combine(Directory.GetCurrentDirectory(), "EditorBoot.log");

    private static int Main()
    {
        try
        {
            File.AppendAllText(BOOT_LOG_PATH, $"[{DateTime.Now:O}] Main start.{Environment.NewLine}");
            using EditorHost host = new();
            int exitCode = host.Run();
            File.AppendAllText(BOOT_LOG_PATH, $"[{DateTime.Now:O}] Main exit code={exitCode}.{Environment.NewLine}");
            return exitCode;
        }
        catch (Exception ex)
        {
            string msg = $"[{DateTime.Now:O}] Unhandled exception:{Environment.NewLine}{ex}{Environment.NewLine}";
            File.AppendAllText(BOOT_LOG_PATH, msg);
            Console.Error.WriteLine(msg);
            return 1;
        }
    }
}
