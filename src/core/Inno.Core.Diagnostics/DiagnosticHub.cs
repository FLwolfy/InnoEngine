using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Inno.Core.Diagnostics;

/// <summary>
/// Owns one host's complete diagnostic state and its presentation subscriptions.
/// </summary>
public sealed class DiagnosticHub
{
    private static readonly AsyncLocal<Scope?> S_CURRENT_SCOPE = new();

    private readonly Dictionary<string, DiagnosticReport> m_reports = new(StringComparer.Ordinal);
    private readonly List<IDiagnosticSink> m_sinks = [];
    private readonly object m_sync = new();

    internal static DiagnosticHub current
        => S_CURRENT_SCOPE.Value?.hub
            ?? throw new InvalidOperationException(
                "No diagnostic hub is bound to the current runtime execution context.");

    /// <summary>
    /// Occurs when a diagnostic presentation sink fails and is quarantined.
    /// </summary>
    public event Action<Exception>? sinkFailed;

    /// <summary>
    /// Binds this hub to the current asynchronous execution context.
    /// </summary>
    /// <returns>
    /// A strict last-in-first-out scope owned by the caller.
    /// </returns>
    public IDisposable EnterScope()
    {
        var scope = new Scope(this, S_CURRENT_SCOPE.Value);
        S_CURRENT_SCOPE.Value = scope;
        return scope;
    }

    /// <summary>
    /// Registers a sink and synchronously replays every active report before registration completes.
    /// </summary>
    /// <param name="sink">
    /// The sink that receives current and future reports.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="sink"/> is null.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the sink cannot accept the current diagnostic snapshot.
    /// </exception>
    public void RegisterSink(IDiagnosticSink sink)
    {
        ArgumentNullException.ThrowIfNull(sink);
        lock (m_sync)
        {
            if (m_sinks.Contains(sink))
                return;
            try
            {
                foreach (DiagnosticReport report in m_reports.Values)
                    sink.Replace(report);
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException(
                    "The diagnostic sink rejected the active snapshot and was not registered.",
                    exception);
            }
            m_sinks.Add(sink);
        }
    }

    /// <summary>
    /// Unregisters a sink so it receives no future diagnostic changes.
    /// </summary>
    /// <param name="sink">
    /// The sink to remove when registered.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="sink"/> is null.
    /// </exception>
    public void UnregisterSink(IDiagnosticSink sink)
    {
        ArgumentNullException.ThrowIfNull(sink);
        lock (m_sync)
            m_sinks.Remove(sink);
    }

    /// <summary>
    /// Atomically replaces the complete diagnostic state published by one producer.
    /// </summary>
    /// <param name="source">
    /// The stable producer identity whose previous report is replaced.
    /// </param>
    /// <param name="diagnostics">
    /// The complete current report, or an empty collection to clear it.
    /// </param>
    /// <exception cref="ArgumentException">
    /// Thrown when the source is invalid or the collection contains a null entry.
    /// </exception>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="diagnostics"/> is <see langword="null"/>.
    /// </exception>
    public void Set(DiagnosticSource source, IEnumerable<Diagnostic> diagnostics)
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
        lock (m_sync)
        {
            m_reports[source.id] = report;
            NotifySinks(static (sink, value) => sink.Replace(value), report);
        }
    }

    /// <summary>
    /// Clears the active report published by one producer.
    /// </summary>
    /// <param name="source">
    /// The stable producer identity to clear.
    /// </param>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="source"/> is invalid.
    /// </exception>
    public void Clear(DiagnosticSource source)
    {
        ValidateSource(source);
        lock (m_sync)
        {
            if (!m_reports.Remove(source.id, out DiagnosticReport? report))
                return;
            NotifySinks(static (sink, value) => sink.Clear(value), report.source);
        }
    }

    private void NotifySinks<TValue>(Action<IDiagnosticSink, TValue> callback, TValue value)
    {
        IDiagnosticSink[] sinks = m_sinks.ToArray();
        for (int index = 0; index < sinks.Length; index++)
        {
            try
            {
                callback(sinks[index], value);
            }
            catch (Exception exception)
            {
                m_sinks.Remove(sinks[index]);
                Action<Exception>? observer = sinkFailed;
                if (observer is null)
                    Console.Error.WriteLine($"Diagnostic sink failed and was quarantined: {exception}");
                else
                    observer(exception);
            }
        }
    }

    private static void ValidateSource(DiagnosticSource source)
    {
        if (string.IsNullOrWhiteSpace(source.id))
            throw new ArgumentException("A valid diagnostic source is required.", nameof(source));
    }

    private sealed class Scope(DiagnosticHub hub, Scope? previous) : IDisposable
    {
        private bool m_disposed;

        internal DiagnosticHub hub { get; } = hub;

        /// <summary>
        /// Releases the resources owned by this instance.
        /// </summary>
        public void Dispose()
        {
            if (m_disposed)
                return;
            if (!ReferenceEquals(S_CURRENT_SCOPE.Value, this))
                throw new InvalidOperationException("Diagnostic hub scopes must be disposed in last-in-first-out order.");
            m_disposed = true;
            S_CURRENT_SCOPE.Value = previous;
        }
    }
}
