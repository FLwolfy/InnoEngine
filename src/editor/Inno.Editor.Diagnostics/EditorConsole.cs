using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

using Inno.Core.Diagnostics;
using Inno.Core.Logging;
using Inno.Editor.PlayMode;

namespace Inno.Editor.Diagnostics;

/// <summary>
/// Owns one editor's bounded Console history, current diagnostics, grouping, and Play Mode clearing policy.
/// </summary>
public sealed class EditorConsole : IEditorConsole, ILogSink, IDiagnosticSink, IDisposable
{
    private readonly Queue<EditorConsoleOccurrence> m_logs = new();
    private readonly Dictionary<string, EditorConsoleOccurrence[]> m_diagnostics = new(StringComparer.Ordinal);
    private readonly DiagnosticHub m_diagnosticHub;
    private readonly LogRouter m_logRouter;
    private readonly IEditorPlayMode m_playMode;
    private readonly object m_sync = new();

    private int m_capacity = 1024;
    private long m_nextSequence;
    private long m_revision;
    private bool m_started;
    private bool m_disposed;

    /// <summary>
    /// Creates a Console service that observes one editor Play Mode controller.
    /// </summary>
    /// <param name="logRouter">
    /// The host logging router whose entries are presented by this Console.
    /// </param>
    /// <param name="diagnosticHub">
    /// The host diagnostic hub whose current reports are presented by this Console.
    /// </param>
    /// <param name="playMode">
    /// The Play Mode source used to clear ordinary logs when a new simulation request begins.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="playMode"/> is <see langword="null"/>.
    /// </exception>
    public EditorConsole(
        LogRouter logRouter,
        DiagnosticHub diagnosticHub,
        IEditorPlayMode playMode)
    {
        m_logRouter = logRouter ?? throw new ArgumentNullException(nameof(logRouter));
        m_diagnosticHub = diagnosticHub ?? throw new ArgumentNullException(nameof(diagnosticHub));
        m_playMode = playMode ?? throw new ArgumentNullException(nameof(playMode));
    }

    /// <summary>
    /// Gets or sets the maximum number of ordinary log occurrences retained in memory.
    /// </summary>
    public int capacity
    {
        get
        {
            lock (m_sync)
                return m_capacity;
        }
        set
        {
            lock (m_sync)
            {
                int normalized = Math.Max(16, value);
                if (normalized == m_capacity)
                    return;
                m_capacity = normalized;
                TrimLogsUnsafe();
                m_revision++;
            }
        }
    }

    /// <summary>
    /// Gets or sets whether ordinary Console logs are cleared when a new Play Mode request begins.
    /// Current diagnostics remain visible because they represent active compiler and subsystem state.
    /// </summary>
    public bool clearOnPlay { get; set; } = true;

    /// <summary>
    /// Attaches this service to the process logging, diagnostic, and Play Mode sources.
    /// </summary>
    /// <exception cref="ObjectDisposedException">
    /// Thrown after this service has been disposed.
    /// </exception>
    public void Start()
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);
        if (m_started)
            return;
        m_logRouter.RegisterSink(this);
        m_diagnosticHub.RegisterSink(this);
        m_playMode.stateChanged += OnPlayModeStateChanged;
        m_started = true;
    }

    /// <summary>
    /// Appends one asynchronously dispatched log entry.
    /// </summary>
    /// <param name="entry">
    /// The immutable entry to append.
    /// </param>
    public void Receive(LogEntry entry)
    {
        lock (m_sync)
        {
            m_logs.Enqueue(FromLog(NextSequence(), entry));
            TrimLogsUnsafe();
            m_revision++;
        }
    }

    /// <summary>
    /// Replaces the complete current diagnostic report for one producer.
    /// </summary>
    /// <param name="report">
    /// The complete report to expose in subsequent snapshots.
    /// </param>
    public void Replace(DiagnosticReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        var entries = new EditorConsoleOccurrence[report.diagnostics.Count];
        for (int i = 0; i < entries.Length; i++)
            entries[i] = FromDiagnostic(NextSequence(), report, report.diagnostics[i]);
        lock (m_sync)
        {
            m_diagnostics[report.source.id] = entries;
            m_revision++;
        }
    }

    /// <summary>
    /// Removes the current diagnostic report for one producer.
    /// </summary>
    /// <param name="source">
    /// The producer whose report should be removed.
    /// </param>
    public void Clear(DiagnosticSource source)
    {
        lock (m_sync)
        {
            if (m_diagnostics.Remove(source.id))
                m_revision++;
        }
    }

    /// <summary>
    /// Captures an immutable snapshot containing both individual occurrences and global groups.
    /// </summary>
    /// <returns>
    /// A consistent snapshot that remains valid while new entries arrive.
    /// </returns>
    public EditorConsoleSnapshot Capture()
    {
        lock (m_sync)
        {
            EditorConsoleOccurrence[] occurrences = m_logs
                .Concat(m_diagnostics.Values.SelectMany(static entries => entries))
                .OrderBy(static occurrence => occurrence.sequence)
                .ToArray();
            EditorConsoleGroup[] groups = occurrences
                .GroupBy(ConsoleFingerprint.Create, StringComparer.Ordinal)
                .Select(static group => new EditorConsoleGroup(group.Key, group.ToArray()))
                .OrderBy(static group => group.latest.sequence)
                .ToArray();
            return new EditorConsoleSnapshot(m_revision, occurrences, groups);
        }
    }

    /// <summary>
    /// Removes all retained logs and current diagnostic reports.
    /// </summary>
    public void Clear()
    {
        lock (m_sync)
        {
            if (m_logs.Count == 0 && m_diagnostics.Count == 0)
                return;
            m_logs.Clear();
            m_diagnostics.Clear();
            m_revision++;
        }
    }

    /// <summary>
    /// Detaches this service and releases its subscriptions.
    /// </summary>
    public void Dispose()
    {
        if (m_disposed)
            return;
        if (m_started)
        {
            m_playMode.stateChanged -= OnPlayModeStateChanged;
            m_logRouter.UnregisterSink(this);
            m_diagnosticHub.UnregisterSink(this);
            m_started = false;
        }
        m_disposed = true;
    }

    private void OnPlayModeStateChanged(EditorPlayModeState state)
    {
        if (state == EditorPlayModeState.Compiling && clearOnPlay)
            ClearLogsForPlay();
    }

    private void ClearLogsForPlay()
    {
        m_logRouter.Flush();
        lock (m_sync)
        {
            if (m_logs.Count == 0)
                return;
            m_logs.Clear();
            m_revision++;
        }
    }

    private static EditorConsoleOccurrence FromLog(long sequence, LogEntry entry)
        => new(
            sequence,
            EditorConsoleEntryKind.Log,
            entry.level,
            $"{entry.domain}/{entry.scope}",
            string.Empty,
            string.Empty,
            entry.category,
            entry.message,
            entry.time,
            entry.file,
            entry.line,
            0,
            entry.stackTrace,
            entry.sessionId);

    private static EditorConsoleOccurrence FromDiagnostic(
        long sequence,
        DiagnosticReport report,
        Diagnostic diagnostic)
    {
        DiagnosticLocation? location = diagnostic.location;
        return new EditorConsoleOccurrence(
            sequence,
            EditorConsoleEntryKind.Diagnostic,
            diagnostic.severity switch
            {
                DiagnosticSeverity.Info => LogLevel.Info,
                DiagnosticSeverity.Warning => LogLevel.Warn,
                DiagnosticSeverity.Error => LogLevel.Error,
                _ => LogLevel.Error
            },
            report.source.displayName,
            report.source.id,
            diagnostic.code,
            report.source.displayName,
            diagnostic.message,
            report.publishedAt,
            location?.sourcePath ?? string.Empty,
            location?.line ?? 0,
            location?.column ?? 0,
            string.Empty,
            LogSessionId.none);
    }

    private long NextSequence()
        => Interlocked.Increment(ref m_nextSequence);

    private void TrimLogsUnsafe()
    {
        while (m_logs.Count > m_capacity)
            _ = m_logs.Dequeue();
    }
}
