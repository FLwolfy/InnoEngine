using System;
using System.Text;

namespace Inno.Editor.Panel.Logging;

internal sealed class ConsoleEntryCopyTarget
{
    internal ConsoleEntryCopyTarget(EditorConsoleEntry entry, int repeatCount)
    {
        message = entry.displayMessage;
        fullText = FormatFullText(entry, repeatCount);
    }

    internal string message { get; }
    internal string fullText { get; }

    private static string FormatFullText(EditorConsoleEntry entry, int repeatCount)
    {
        var builder = new StringBuilder();
        builder.Append('[')
            .Append(entry.time.ToString("yyyy-MM-dd HH:mm:ss.fff"))
            .Append("] [")
            .Append(entry.kind)
            .Append("] [")
            .Append(entry.level)
            .Append("] [")
            .Append(entry.category)
            .Append(']');
        builder.Append(' ').Append(entry.displayMessage);
        if (repeatCount > 1)
            builder.Append(" (x").Append(repeatCount).Append(')');
        if (!string.IsNullOrWhiteSpace(entry.file))
        {
            builder.AppendLine();
            builder.Append(entry.file);
            if (entry.line > 0)
            {
                builder.Append('(').Append(entry.line);
                if (entry.column > 0)
                    builder.Append(',').Append(entry.column);
                builder.Append(')');
            }
        }
        return builder.ToString();
    }
}
