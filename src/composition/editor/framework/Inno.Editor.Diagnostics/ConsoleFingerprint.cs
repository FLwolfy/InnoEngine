using System;
using System.Security.Cryptography;
using System.Text;

namespace Inno.Editor.Diagnostics;

internal static class ConsoleFingerprint
{
    internal static string Create(EditorConsoleOccurrence occurrence)
    {
        var builder = new StringBuilder();
        Append(builder, occurrence.kind.ToString());
        Append(builder, occurrence.level.ToString());
        Append(builder, occurrence.sourceId);
        Append(builder, occurrence.source);
        Append(builder, occurrence.code);
        Append(builder, occurrence.category);
        Append(builder, occurrence.message);
        Append(builder, occurrence.file);
        Append(builder, occurrence.line.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Append(builder, occurrence.column.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Append(builder, occurrence.stackTrace);
        Append(builder, occurrence.sessionId.ToString());
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())));
    }

    private static void Append(StringBuilder builder, string value)
        => builder.Append(value.Length).Append(':').Append(value).Append('|');
}
