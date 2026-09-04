using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using Inno.Extensibility.Modules;

namespace Inno.Core.Logging;

/// <summary>
/// Writes category-bound entries through one explicitly owned log router.
/// </summary>
public sealed class Logger
{
    private readonly LogRouter m_router;
    private readonly AssemblyDomain m_domain;
    private readonly AssemblyScope m_scope;
    private readonly string m_category;

    internal Logger(
        LogRouter router,
        AssemblyDomain domain,
        AssemblyScope scope,
        string category)
    {
        m_router = router;
        m_domain = domain;
        m_scope = scope;
        m_category = category;
    }

    /// <summary>
    /// Writes one formatted entry with source information captured from the caller.
    /// </summary>
    /// <param name="level">
    /// The severity assigned to the entry.
    /// </param>
    /// <param name="message">
    /// The composite format string or final message text.
    /// </param>
    /// <param name="arguments">
    /// Optional composite-format arguments.
    /// </param>
    /// <param name="filePath">
    /// The source file path supplied by the compiler.
    /// </param>
    /// <param name="lineNumber">
    /// The source line number supplied by the compiler.
    /// </param>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="message"/> is empty.
    /// </exception>
    /// <exception cref="ObjectDisposedException">
    /// Thrown after the owning router has been disposed.
    /// </exception>
    public void Write(
        LogLevel level,
        string message,
        IReadOnlyList<object?>? arguments = null,
        [CallerFilePath] string filePath = "",
        [CallerLineNumber] int lineNumber = 0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        if (!m_router.IsEnabled(level))
            return;
        string rendered = arguments is null || arguments.Count == 0
            ? message
            : string.Format(message, arguments.ToArray());
        m_router.Dispatch(new LogEntry(
            level,
            m_domain,
            m_scope,
            m_category,
            rendered,
            string.IsNullOrWhiteSpace(filePath) ? "Unknown" : Path.GetFullPath(filePath),
            lineNumber,
            new StackTrace(1, true).ToString(),
            LogSessionContext.current));
    }
}
