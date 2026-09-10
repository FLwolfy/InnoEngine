using System;

using Inno.Extensibility.Modules;

namespace Inno.Core.Logging;

/// <summary>
/// Represents an immutable log message dispatched by a <see cref="LogRouter"/>.
/// </summary>
/// <param name="level">
/// The severity of the log message.
/// </param>
/// <param name="domain">
/// The resolved assembly ownership domain.
/// </param>
/// <param name="scope">
/// The resolved runtime or editor scope.
/// </param>
/// <param name="category">
/// The log category, typically the declaring type name.
/// </param>
/// <param name="message">
/// The rendered log message text.
/// </param>
/// <param name="file">
/// The source file name when available.
/// </param>
/// <param name="line">
/// The source line number when available.
/// </param>
/// <param name="stackTrace">
/// The captured managed stack trace, or an empty string when no trace is available.
/// </param>
/// <param name="sessionId">
/// The isolated runtime session that produced the entry, or <see cref="LogSessionId.none"/> for process-level work.
/// </param>
public readonly struct LogEntry(
    LogLevel level,
    AssemblyDomain domain,
    AssemblyScope scope,
    string category,
    string message,
    string file,
    int line,
    string stackTrace,
    LogSessionId sessionId
) {
    /// <summary>
    /// Gets the severity of this entry.
    /// </summary>
    public readonly LogLevel level = level;

    /// <summary>
    /// Gets the assembly ownership domain for this entry.
    /// </summary>
    public readonly AssemblyDomain domain = domain;

    /// <summary>
    /// Gets the runtime or editor scope for this entry.
    /// </summary>
    public readonly AssemblyScope scope = scope;

    /// <summary>
    /// Gets the category name for this entry.
    /// </summary>
    public readonly string category = category;

    /// <summary>
    /// Gets the rendered message text.
    /// </summary>
    public readonly string message = message;

    /// <summary>
    /// Gets the timestamp captured when this entry was created.
    /// </summary>
    public readonly DateTime time = DateTime.Now;

    /// <summary>
    /// Gets the source file name if available; otherwise a fallback name.
    /// </summary>
    public readonly string file = file;

    /// <summary>
    /// Gets the source line number if available.
    /// </summary>
    public readonly int line = line;

    /// <summary>
    /// Gets the managed stack trace captured at the logging call site.
    /// </summary>
    public readonly string stackTrace = stackTrace;

    /// <summary>
    /// Gets the isolated runtime session that produced this entry.
    /// </summary>
    public readonly LogSessionId sessionId = sessionId;
}
