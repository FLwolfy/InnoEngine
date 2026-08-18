using System;

using Inno.Core.Assemblies;

namespace Inno.Core.Logging;

/// <summary>
/// Represents an immutable log message dispatched by <see cref="LogManager"/>.
/// </summary>
/// <param name="level">The severity of the log message.</param>
/// <param name="source">The resolved assembly group source.</param>
/// <param name="category">The log category, typically the declaring type name.</param>
/// <param name="message">The rendered log message text.</param>
/// <param name="file">The source file name when available.</param>
/// <param name="line">The source line number when available.</param>
public readonly struct LogEntry(
    LogLevel level,
    AssemblyGroup source,
    string category,
    string message,
    string file,
    int line
) {
    /// <summary>
    /// Gets the severity of this entry.
    /// </summary>
    public readonly LogLevel level = level;

    /// <summary>
    /// Gets the assembly group source for this entry.
    /// </summary>
    public readonly AssemblyGroup source = source;

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
}
