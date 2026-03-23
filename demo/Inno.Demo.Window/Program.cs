using System;

namespace Inno.Demo.Window;

internal static class Program
{
    private static int Main()
    {
        if (!OperatingSystem.IsMacOS())
        {
            Console.Error.WriteLine("Inno.Demo.Window currently supports macOS only.");
            return 1;
        }

        return 0;
    }
}
