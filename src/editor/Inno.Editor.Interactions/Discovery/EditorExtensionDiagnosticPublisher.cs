using System;
using System.Collections.Generic;
using System.Linq;

using Inno.Core.Diagnostics;

namespace Inno.Editor.Interactions;

internal sealed class EditorExtensionDiagnosticPublisher : IDisposable
{
    private const string C_PANEL_ACTIVATION_GROUP = "Panel Activation";

    private readonly Dictionary<string, string> m_panelFailures = new(StringComparer.Ordinal);
    private string m_publishedState = string.Empty;

    internal void ReportPanelFailure(string panelId, Exception exception)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(panelId);
        ArgumentNullException.ThrowIfNull(exception);
        m_panelFailures[panelId] = exception.Message;
    }

    internal void ResolvePanel(string panelId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(panelId);
        m_panelFailures.Remove(panelId);
    }

    internal void RetainPanels(IReadOnlySet<string> panelIds)
    {
        ArgumentNullException.ThrowIfNull(panelIds);
        string[] removed = m_panelFailures.Keys
            .Where(id => !panelIds.Contains(id))
            .ToArray();
        for (int i = 0; i < removed.Length; i++)
            m_panelFailures.Remove(removed[i]);
    }

    internal void Commit()
    {
        if (m_panelFailures.Count == 0)
        {
            if (!string.IsNullOrEmpty(m_publishedState))
                Diagnostics.Clear(C_PANEL_ACTIVATION_GROUP);
            m_publishedState = string.Empty;
            return;
        }

        KeyValuePair<string, string>[] failures = m_panelFailures
            .OrderBy(static pair => pair.Key, StringComparer.Ordinal)
            .ToArray();
        string state = string.Join('\n', failures.Select(static pair => $"{pair.Key}:{pair.Value}"));
        if (string.Equals(m_publishedState, state, StringComparison.Ordinal))
            return;
        Diagnostics.Set(
            C_PANEL_ACTIVATION_GROUP,
            failures.Select(static pair => Diagnostic.Error(
                "EDITOR-PANEL",
                $"Panel '{pair.Key}' failed to attach: {pair.Value}")));
        m_publishedState = state;
    }

    /// <summary>
    /// Releases the resources owned by this instance.
    /// </summary>
    public void Dispose()
    {
        m_panelFailures.Clear();
        if (!string.IsNullOrEmpty(m_publishedState))
            Diagnostics.Clear(C_PANEL_ACTIVATION_GROUP);
        m_publishedState = string.Empty;
    }
}
