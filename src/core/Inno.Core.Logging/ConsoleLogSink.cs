using System;

namespace Inno.Core.Logging;

public class ConsoleLogSink : ILogSink
{
    public void Receive(LogEntry entry)
    {
        var originalColor = Console.ForegroundColor;

        Console.ForegroundColor = entry.level switch
        {
            LogLevel.Debug => ConsoleColor.DarkGray,
            LogLevel.Info  => ConsoleColor.Green,
            LogLevel.Warn  => ConsoleColor.Yellow,
            LogLevel.Error => ConsoleColor.Red,
            LogLevel.Fatal => ConsoleColor.Magenta,
            _ => ConsoleColor.White
        };
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] <{entry.source}> [{entry.category}]: {entry.message}");

        Console.ForegroundColor = originalColor;
    }
}