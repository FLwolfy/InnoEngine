using System;

namespace Inno.Player;

internal static class Program
{
    private static int Main(string[] arguments)
    {
        try
        {
            int? smokeFrameLimit = ParseSmokeFrameLimit(arguments);
            using var host = GamePlayerHost.Create();
            return host.Run(smokeFrameLimit);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static int? ParseSmokeFrameLimit(string[] arguments)
    {
        if (arguments.Length == 0)
            return null;
        if (arguments.Length != 2
            || !string.Equals(arguments[0], "--smoke-frames", StringComparison.Ordinal)
            || !int.TryParse(arguments[1], out int frameCount)
            || frameCount <= 0)
        {
            throw new ArgumentException("Usage: Inno.Player [--smoke-frames <positive-count>].");
        }
        return frameCount;
    }
}
