using System;
using System.Collections.Generic;
using System.Linq;

namespace Inno.Core.Diagnose;

/// <summary>
/// Coordinates the complete current diagnostic state published by independent producers.
/// </summary>
public static class DiagnosticManager
{
    private static readonly Dictionary<string, DiagnosticReport> REPORTS = new(StringComparer.Ordinal);
    private static readonly List<IDiagnosticSink> SINKS = [];
    private static readonly object SYNC = new();

    /// <summary>
    /// Registers a sink and immediately replays every active diagnostic report to it.
    /// </summary>
    /// <param name="sink">The sink that should receive current and future reports.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="sink"/> is <see langword="null"/>.</exception>
    public static void RegisterSink(IDiagnosticSink sink)
    {
        ArgumentNullException.ThrowIfNull(sink);
        lock (SYNC)
        {
            if (SINKS.Contains(sink))
                return;
            SINKS.Add(sink);
            DiagnosticReport[] reports = REPORTS.Values.ToArray();
            for (int i = 0; i < reports.Length; i++)
                ReplaceSafely(sink, reports[i]);
        }
    }

    /// <summary>
    /// Unregisters a sink so that it no longer receives diagnostic state changes.
    /// </summary>
    /// <param name="sink">The sink to unregister.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="sink"/> is <see langword="null"/>.</exception>
    public static void UnregisterSink(IDiagnosticSink sink)
    {
        ArgumentNullException.ThrowIfNull(sink);
        lock (SYNC)
        {
            SINKS.Remove(sink);
        }
    }

    internal static void Set(DiagnosticSource source, IEnumerable<Diagnostic> diagnostics)
    {
        ValidateSource(source);
        ArgumentNullException.ThrowIfNull(diagnostics);
        Diagnostic[] entries = diagnostics.ToArray();
        if (entries.Length == 0)
        {
            Clear(source);
            return;
        }
        if (entries.Any(static diagnostic => diagnostic is null))
            throw new ArgumentException("A diagnostic collection cannot contain null entries.", nameof(diagnostics));

        var report = new DiagnosticReport(source, Array.AsReadOnly(entries), DateTime.Now);
        lock (SYNC)
        {
            REPORTS[source.id] = report;
            IDiagnosticSink[] sinks = SINKS.ToArray();
            for (int i = 0; i < sinks.Length; i++)
                ReplaceSafely(sinks[i], report);
        }
    }

    internal static void Clear(DiagnosticSource source)
    {
        ValidateSource(source);
        lock (SYNC)
        {
            if (!REPORTS.Remove(source.id, out DiagnosticReport? report))
                return;
            IDiagnosticSink[] sinks = SINKS.ToArray();
            for (int i = 0; i < sinks.Length; i++)
                ClearSafely(sinks[i], report.source);
        }
    }

    private static void ValidateSource(DiagnosticSource source)
    {
        if (string.IsNullOrWhiteSpace(source.id))
            throw new ArgumentException("A valid diagnostic source is required.", nameof(source));
    }

    private static void ReplaceSafely(IDiagnosticSink sink, DiagnosticReport report)
    {
        try
        {
            sink.Replace(report);
        }
        catch
        {
            // A diagnostic presentation failure must not affect the producer or other sinks.
        }
    }

    private static void ClearSafely(IDiagnosticSink sink, DiagnosticSource source)
    {
        try
        {
            sink.Clear(source);
        }
        catch
        {
            // A diagnostic presentation failure must not affect the producer or other sinks.
        }
    }
}
