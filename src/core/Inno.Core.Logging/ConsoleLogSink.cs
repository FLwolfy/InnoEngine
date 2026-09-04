using System;

namespace Inno.Core.Logging;

/// <summary>
/// Writes log entries to the process console with level-based colors.
/// </summary>
public class ConsoleLogSink : ILogSink
{
    /// <summary>
    /// Writes the specified entry to standard output.
    /// </summary>
    /// <param name="entry">
    /// The log entry to print.
    /// </param>
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
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] <{entry.domain}/{entry.scope}> [{entry.category}]: {entry.message}");

        Console.ForegroundColor = originalColor;
    }
}
