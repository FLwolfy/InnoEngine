using System;
using System.Collections.Generic;
using System.Linq;
using Inno.Core.Diagnostics;
using Inno.Extensibility.Modules;

namespace Inno.Core.Logging;

/// <summary>
/// Projects changed diagnostic issues into a host's log stream without introducing another diagnostic owner.
/// </summary>
public sealed class DiagnosticLogSink : IDiagnosticSink, IDisposable
{
    private readonly DiagnosticHub m_hub;
    private readonly LogRouter m_logs;
    private readonly Dictionary<string, Diagnostic[]> m_previous = new(StringComparer.Ordinal);
    private bool m_disposed;

    /// <summary>
    /// Registers a log presentation for all current and future reports in one hub.
    /// </summary>
    /// <param name="hub">
    /// The authoritative diagnostic owner.
    /// </param>
    /// <param name="logs">
    /// The borrowed destination router, which must outlive this sink.
    /// </param>
    public DiagnosticLogSink(DiagnosticHub hub, LogRouter logs)
    {
        m_hub = hub ?? throw new ArgumentNullException(nameof(hub));
        m_logs = logs ?? throw new ArgumentNullException(nameof(logs));
        m_hub.RegisterSink(this);
    }
    /// <summary>
    /// Replaces the current report for one diagnostic source.
    /// </summary>
    /// <param name="report">
    /// The complete current report to store or present.
    /// </param>
    public void Replace(DiagnosticReport report)
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);
        Diagnostic[] previous = m_previous.GetValueOrDefault(report.source.id, []);
        foreach (Diagnostic issue in report.diagnostics)
        {
            if (previous.Any(old => old.code == issue.code && old.semanticId == issue.semanticId &&
                old.objectId == issue.objectId && old.message == issue.message && old.severity == issue.severity &&
                Equals(old.location, issue.location)))
                continue;
            LogLevel level = issue.severity switch
            {
                DiagnosticSeverity.Error => LogLevel.Error,
                DiagnosticSeverity.Warning => LogLevel.Warn,
                _ => LogLevel.Info
            };
            m_logs.Dispatch(new LogEntry(level, AssemblyDomain.InnoInternal, AssemblyScope.Runtime,
                report.source.displayName, $"{issue.code}: {issue.message}",
                issue.location?.sourcePath ?? string.Empty, issue.location?.line ?? 0,
                string.Empty, LogSessionContext.current));
        }
        m_previous[report.source.id] = report.diagnostics.ToArray();
    }
    /// <summary>
    /// Removes the current report for one diagnostic source.
    /// </summary>
    /// <param name="source">
    /// The source whose current report was cleared.
    /// </param>
    public void Clear(DiagnosticSource source) => m_previous.Remove(source.id);

    /// <summary>
    /// Unregisters this presentation and releases its neutral de-duplication snapshot.
    /// </summary>
    public void Dispose()
    {
        if (m_disposed)
            return;
        m_hub.UnregisterSink(this);
        m_disposed = true;
        m_previous.Clear();
    }
}
