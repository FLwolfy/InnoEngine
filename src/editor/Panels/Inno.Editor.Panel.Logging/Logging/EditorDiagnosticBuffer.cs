using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

using Inno.Core.Diagnostics;

namespace Inno.Editor.Panel.Logging;

internal sealed class EditorDiagnosticBuffer : IDiagnosticSink
{
    private readonly Dictionary<string, EditorDiagnosticEntry[]> m_sources = new(StringComparer.Ordinal);
    private readonly object m_sync = new();

    private long m_nextId;
    private long m_version;

    public void Replace(DiagnosticReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        var entries = new EditorDiagnosticEntry[report.diagnostics.Count];
        for (int i = 0; i < report.diagnostics.Count; i++)
        {
            Diagnostic diagnostic = report.diagnostics[i];
            DiagnosticLocation? location = diagnostic.location;
            entries[i] = new EditorDiagnosticEntry(
                Interlocked.Decrement(ref m_nextId),
                report.source,
                report.publishedAt,
                diagnostic.code,
                diagnostic.severity,
                diagnostic.message,
                location?.sourcePath ?? string.Empty,
                location?.line ?? 0,
                location?.column ?? 0);
        }
        lock (m_sync)
        {
            if (entries.Length == 0)
                m_sources.Remove(report.source.id);
            else
                m_sources[report.source.id] = entries;
            m_version++;
        }
    }

    public void Clear(DiagnosticSource source)
    {
        lock (m_sync)
        {
            if (!m_sources.Remove(source.id))
                return;
            m_version++;
        }
    }

    internal EditorDiagnosticEntry[] Snapshot(out long version)
    {
        lock (m_sync)
        {
            version = m_version;
            return m_sources.Values
                .SelectMany(static entries => entries)
                .OrderBy(static entry => entry.time)
                .ThenByDescending(static entry => entry.id)
                .ToArray();
        }
    }

    internal void Clear()
    {
        lock (m_sync)
        {
            if (m_sources.Count == 0)
                return;
            m_sources.Clear();
            m_version++;
        }
    }
}
