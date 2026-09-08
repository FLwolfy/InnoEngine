using System;
using System.Collections.Generic;
using System.Linq;

namespace Inno.Core.Diagnostics;

/// <summary>
/// Owns one revocable diagnostic producer registration and releases its current report on retirement.
/// </summary>
public sealed class DiagnosticReporter : IDiagnosticReporter, IDisposable
{
    private readonly object m_sync;
    private readonly DiagnosticSource m_source;
    private readonly long m_epoch;
    private readonly Dictionary<IssueKey, Diagnostic> m_issues = [];
    private DiagnosticHub? m_hub;

    internal DiagnosticReporter(DiagnosticHub hub, DiagnosticSource source, long epoch)
    {
        m_hub = hub;
        m_sync = hub.synchronizationRoot;
        m_source = source;
        m_epoch = epoch;
    }
    /// <summary>
    /// Replaces the issue with the same code and target, without using its message as identity.
    /// </summary>
    /// <param name="diagnostic">
    /// The immutable current issue.
    /// </param>
    /// <exception cref="ObjectDisposedException">
    /// This producer has retired or been replaced.
    /// </exception>
    public void Publish(Diagnostic diagnostic)
    {
        ArgumentNullException.ThrowIfNull(diagnostic);
        lock (m_sync)
        {
            DiagnosticHub hub = GetHub();
            m_issues[new(diagnostic.code, diagnostic.semanticId, diagnostic.objectId)] = diagnostic;
            hub.SetOwned(m_source, m_epoch, Ordered());
        }
    }
    /// <summary>
    /// Resolves an issue after the represented condition no longer exists.
    /// </summary>
    /// <param name="code">
    /// The stable issue code.
    /// </param>
    /// <param name="semanticId">
    /// The optional protocol identifier used at publication.
    /// </param>
    /// <param name="objectId">
    /// The optional persistent object identity used at publication.
    /// </param>
    /// <exception cref="ObjectDisposedException">
    /// This producer has retired or been replaced.
    /// </exception>
    public void Resolve(string code, string? semanticId = null, Guid? objectId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        lock (m_sync)
        {
            DiagnosticHub hub = GetHub();
            m_issues.Remove(new(code, semanticId, objectId));
            hub.SetOwned(m_source, m_epoch, Ordered());
        }
    }
    /// <summary>
    /// Atomically replaces this producer's complete current issue set.
    /// </summary>
    /// <param name="diagnostics">
    /// The full immutable issue set; empty clears the report.
    /// </param>
    /// <exception cref="ArgumentException">
    /// The supplied issue set contains a duplicate identity.
    /// </exception>
    /// <exception cref="ObjectDisposedException">
    /// This producer has retired or been replaced.
    /// </exception>
    public void Replace(IEnumerable<Diagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        var candidate = new Dictionary<IssueKey, Diagnostic>();
        foreach (Diagnostic diagnostic in diagnostics)
        {
            ArgumentNullException.ThrowIfNull(diagnostic);
            if (!candidate.TryAdd(new(diagnostic.code, diagnostic.semanticId, diagnostic.objectId), diagnostic))
                throw new ArgumentException("A complete diagnostic report contains duplicate issue identities.", nameof(diagnostics));
        }
        lock (m_sync)
        {
            DiagnosticHub hub = GetHub();
            hub.SetOwned(m_source, m_epoch, candidate.Values.ToArray());
            m_issues.Clear();
            foreach (var entry in candidate)
                m_issues.Add(entry.Key, entry.Value);
        }
    }

    /// <summary>
    /// Revokes this registration without clearing a newer registration for the same producer.
    /// </summary>
    public void Dispose()
    {
        lock (m_sync)
        {
            DiagnosticHub? hub = m_hub;
            m_hub = null;
            m_issues.Clear();
            hub?.ReleaseOwned(m_source, m_epoch);
        }
    }

    private DiagnosticHub GetHub()
        => m_hub ?? throw new ObjectDisposedException(nameof(DiagnosticReporter));

    private Diagnostic[] Ordered() => m_issues.Values
        .OrderBy(static issue => issue.code, StringComparer.Ordinal)
        .ThenBy(static issue => issue.semanticId, StringComparer.Ordinal)
        .ThenBy(static issue => issue.objectId).ToArray();

    private readonly record struct IssueKey(string code, string? semanticId, Guid? objectId);
}
