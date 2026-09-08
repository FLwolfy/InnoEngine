using Inno.Core.Execution;
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
    private static readonly ExecutionSlot<DiagnosticHub> S_CURRENT_SCOPE = new("hub");

    private readonly Dictionary<string, DiagnosticReport> m_reports = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> m_producerEpochs = new(StringComparer.Ordinal);
    private long m_nextProducerEpoch;
    private readonly List<IDiagnosticSink> m_sinks = [];
    private readonly object m_sync = new();

    internal static DiagnosticHub current
        => S_CURRENT_SCOPE.current;

    internal object synchronizationRoot => m_sync;

    /// <summary>
    /// Occurs when a diagnostic presentation sink fails and is quarantined.
    /// </summary>
    public event Action<Exception>? sinkFailed;

    /// <summary>
    /// Activates a producer at an owner safe point, revoking any previous registration with the same ID.
    /// </summary>
    /// <param name="source">
    /// The stable owner-qualified producer identity.
    /// </param>
    /// <returns>
    /// A producer whose disposal clears only its own active registration.
    /// </returns>
    /// <exception cref="ArgumentException">
    /// The source is not initialized.
    /// </exception>
    public DiagnosticReporter CreateReporter(DiagnosticSource source)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source.id);
        lock (m_sync)
        {
            long epoch = checked(++m_nextProducerEpoch);
            m_producerEpochs[source.id] = epoch;
            Clear(source);
            return new DiagnosticReporter(this, source, epoch);
        }
    }

    internal void SetOwned(DiagnosticSource source, long epoch, IReadOnlyList<Diagnostic> diagnostics)
    {
        lock (m_sync)
        {
            if (!m_producerEpochs.TryGetValue(source.id, out long current) || current != epoch)
                throw new ObjectDisposedException(nameof(DiagnosticReporter), "The diagnostic producer generation has retired.");
            Set(source, diagnostics);
        }
    }

    internal void ReleaseOwned(DiagnosticSource source, long epoch)
    {
        lock (m_sync)
        {
            if (!m_producerEpochs.TryGetValue(source.id, out long current) || current != epoch)
                return;
            m_producerEpochs.Remove(source.id);
            Clear(source);
        }
    }

    /// <summary>
    /// Binds this hub to the current asynchronous execution context.
    /// </summary>
    /// <returns>
    /// A strict last-in-first-out scope owned by the caller.
    /// </returns>
    public IDisposable EnterScope()
    {
        return S_CURRENT_SCOPE.Enter(this);
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
                {
                    foreach (Action<Exception> handler in observer.GetInvocationList())
                    {
                        try { handler(exception); }
                        catch (Exception observerFailure)
                        {
                            Console.Error.WriteLine($"Diagnostic failure observer failed: {observerFailure}");
                        }
                    }
                }
            }
        }
    }

    private static void ValidateSource(DiagnosticSource source)
    {
        if (string.IsNullOrWhiteSpace(source.id))
            throw new ArgumentException("A valid diagnostic source is required.", nameof(source));
    }

}
