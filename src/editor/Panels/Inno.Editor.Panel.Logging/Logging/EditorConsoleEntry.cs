using System;

using Inno.Core.Diagnose;
using Inno.Core.Logging;

namespace Inno.Editor.Panel.Logging;

internal readonly struct EditorConsoleEntry
{
    private EditorConsoleEntry(
        long id,
        EditorConsoleEntryKind kind,
        string diagnosticSourceId,
        string code,
        LogLevel level,
        string source,
        string category,
        string message,
        DateTime time,
        string file,
        int line,
        int column)
    {
        this.id = id;
        this.kind = kind;
        this.diagnosticSourceId = diagnosticSourceId;
        this.code = code;
        this.level = level;
        this.source = source;
        this.category = category;
        this.message = message;
        this.time = time;
        this.file = file;
        this.line = line;
        this.column = column;
    }

    internal long id { get; }
    internal EditorConsoleEntryKind kind { get; }
    internal string diagnosticSourceId { get; }
    internal string code { get; }
    internal LogLevel level { get; }
    internal string source { get; }
    internal string category { get; }
    internal string message { get; }
    internal DateTime time { get; }
    internal string file { get; }
    internal int line { get; }
    internal int column { get; }
    internal string displayMessage => string.IsNullOrWhiteSpace(code) ? message : $"{code}: {message}";

    internal static EditorConsoleEntry FromLog(BufferedLogEntry buffered)
    {
        LogEntry entry = buffered.entry;
        return new EditorConsoleEntry(
            buffered.id,
            EditorConsoleEntryKind.Log,
            diagnosticSourceId: string.Empty,
            code: string.Empty,
            entry.level,
            entry.source.ToString(),
            entry.category,
            entry.message,
            entry.time,
            entry.file,
            entry.line,
            column: 0);
    }

    internal static EditorConsoleEntry FromDiagnostic(EditorDiagnosticEntry entry)
        => new(
            entry.id,
            EditorConsoleEntryKind.Diagnostic,
            entry.source.id,
            entry.code,
            entry.severity switch
            {
                DiagnosticSeverity.Info => LogLevel.Info,
                DiagnosticSeverity.Warning => LogLevel.Warn,
                DiagnosticSeverity.Error => LogLevel.Error,
                _ => LogLevel.Error
            },
            entry.source.displayName,
            entry.source.displayName,
            entry.message,
            entry.time,
            entry.file,
            entry.line,
            entry.column);
}

internal enum EditorConsoleEntryKind
{
    Log,
    Diagnostic
}
