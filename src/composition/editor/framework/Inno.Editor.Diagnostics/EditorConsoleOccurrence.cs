using System;

using Inno.Core.Logging;

namespace Inno.Editor.Diagnostics;

/// <summary>
/// Describes one immutable log or diagnostic occurrence in the editor Console timeline.
/// </summary>
public sealed class EditorConsoleOccurrence
{
    internal EditorConsoleOccurrence(
        long sequence,
        EditorConsoleEntryKind kind,
        LogLevel level,
        string source,
        string sourceId,
        string code,
        string category,
        string message,
        DateTime time,
        string file,
        int line,
        int column,
        string stackTrace,
        LogSessionId sessionId)
    {
        this.sequence = sequence;
        this.kind = kind;
        this.level = level;
        this.source = source;
        this.sourceId = sourceId;
        this.code = code;
        this.category = category;
        this.message = message;
        this.time = time;
        this.file = file;
        this.line = line;
        this.column = column;
        this.stackTrace = stackTrace;
        this.sessionId = sessionId;
    }

    /// <summary>
    /// Gets the process-local monotonic sequence assigned when this occurrence entered the Console.
    /// </summary>
    public long sequence { get; }

    /// <summary>
    /// Gets the source protocol represented by this occurrence.
    /// </summary>
    public EditorConsoleEntryKind kind { get; }

    /// <summary>
    /// Gets the normalized Console severity.
    /// </summary>
    public LogLevel level { get; }

    /// <summary>
    /// Gets the human-readable producer name.
    /// </summary>
    public string source { get; }

    /// <summary>
    /// Gets the stable diagnostic producer identifier, or an empty string for log entries.
    /// </summary>
    public string sourceId { get; }

    /// <summary>
    /// Gets the diagnostic code, or an empty string for uncoded entries.
    /// </summary>
    public string code { get; }

    /// <summary>
    /// Gets the producer-defined category.
    /// </summary>
    public string category { get; }

    /// <summary>
    /// Gets the original message text.
    /// </summary>
    public string message { get; }

    /// <summary>
    /// Gets the message text prefixed by its diagnostic code when one is present.
    /// </summary>
    public string displayMessage => string.IsNullOrWhiteSpace(code) ? message : $"{code}: {message}";

    /// <summary>
    /// Gets the occurrence timestamp.
    /// </summary>
    public DateTime time { get; }

    /// <summary>
    /// Gets the related source path, or an empty string when unavailable.
    /// </summary>
    public string file { get; }

    /// <summary>
    /// Gets the one-based source line, or zero when unavailable.
    /// </summary>
    public int line { get; }

    /// <summary>
    /// Gets the one-based source column, or zero when unavailable.
    /// </summary>
    public int column { get; }

    /// <summary>
    /// Gets the captured stack trace, or an empty string when unavailable.
    /// </summary>
    public string stackTrace { get; }

    /// <summary>
    /// Gets the isolated runtime session that emitted this occurrence.
    /// </summary>
    public LogSessionId sessionId { get; }
}
