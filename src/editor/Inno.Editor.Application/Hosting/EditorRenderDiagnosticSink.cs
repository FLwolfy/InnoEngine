using System;
using System.Collections.Generic;
using System.Linq;
using Inno.Core.Diagnostics;
using Inno.Rendering;

namespace Inno.Editor.Application;

internal sealed class EditorRenderDiagnosticSink : IRenderDiagnosticSink, IDisposable
{
    private static readonly DiagnosticSource S_SOURCE = new(
        "inno.editor.rendering",
        "Rendering");

    private readonly Dictionary<DiagnosticIdentity, RenderDiagnostic> m_active = [];
    private readonly DiagnosticHub m_diagnostics;
    private readonly object m_sync = new();
    private bool m_disposed;

    internal EditorRenderDiagnosticSink(DiagnosticHub diagnostics)
    {
        m_diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
    }

    /// <summary>
    /// Publishes or replaces one current rendering diagnostic.
    /// </summary>
    /// <param name="diagnostic">
    /// The current rendering issue keyed by its code and optional source identity.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="diagnostic"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ObjectDisposedException">
    /// Thrown when this sink has already been disposed.
    /// </exception>
    public void Publish(RenderDiagnostic diagnostic)
    {
        ArgumentNullException.ThrowIfNull(diagnostic);
        lock (m_sync)
        {
            ObjectDisposedException.ThrowIf(m_disposed, this);
            var identity = new DiagnosticIdentity(
                diagnostic.code,
                diagnostic.sourceId,
                diagnostic.message,
                diagnostic.severity);
            if (m_active.ContainsKey(identity))
                return;
            m_active[identity] = diagnostic;
            PublishSnapshot();
        }
    }

    /// <summary>
    /// Removes one current rendering diagnostic after its underlying condition has recovered.
    /// </summary>
    /// <param name="code">
    /// The stable machine-readable code of the diagnostic to remove.
    /// </param>
    /// <param name="sourceId">
    /// The same optional source identity used when the diagnostic was published.
    /// </param>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="code"/> is empty or contains only whitespace.
    /// </exception>
    /// <exception cref="ObjectDisposedException">
    /// Thrown when this sink has already been disposed.
    /// </exception>
    public void Resolve(string code, string? sourceId = null)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new ArgumentException("A rendering diagnostic code is required.", nameof(code));
        lock (m_sync)
        {
            ObjectDisposedException.ThrowIf(m_disposed, this);
            DiagnosticIdentity[] matches = m_active.Keys
                .Where(identity =>
                    string.Equals(identity.code, code, StringComparison.Ordinal) &&
                    string.Equals(identity.sourceId, sourceId, StringComparison.Ordinal))
                .ToArray();
            if (matches.Length == 0)
                return;
            for (int index = 0; index < matches.Length; index++)
                m_active.Remove(matches[index]);
            PublishSnapshot();
        }
    }

    /// <summary>
    /// Clears every rendering diagnostic owned by this sink and releases it.
    /// </summary>
    public void Dispose()
    {
        lock (m_sync)
        {
            if (m_disposed)
                return;
            m_disposed = true;
            m_active.Clear();
            m_diagnostics.Clear(S_SOURCE);
        }
    }

    private void PublishSnapshot()
    {
        if (m_active.Count == 0)
        {
            m_diagnostics.Clear(S_SOURCE);
            return;
        }
        m_diagnostics.Set(
            S_SOURCE,
            m_active.Values
                .OrderBy(static diagnostic => diagnostic.code, StringComparer.Ordinal)
                .ThenBy(static diagnostic => diagnostic.sourceId, StringComparer.Ordinal)
                .Select(ToDiagnostic));
    }

    private static Diagnostic ToDiagnostic(RenderDiagnostic diagnostic)
    {
        DiagnosticLocation? location = string.IsNullOrWhiteSpace(diagnostic.sourceId)
            ? null
            : new DiagnosticLocation(diagnostic.sourceId);
        return diagnostic.severity switch
        {
            RenderDiagnosticSeverity.Error => Diagnostic.Error(
                diagnostic.code,
                diagnostic.message,
                location),
            RenderDiagnosticSeverity.Warning => Diagnostic.Warning(
                diagnostic.code,
                diagnostic.message,
                location),
            _ => Diagnostic.Info(
                diagnostic.code,
                diagnostic.message,
                location)
        };
    }

    private readonly record struct DiagnosticIdentity(
        string code,
        string? sourceId,
        string message,
        RenderDiagnosticSeverity severity);
}
