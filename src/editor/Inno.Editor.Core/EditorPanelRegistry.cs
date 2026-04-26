using System;
using System.Collections.Generic;

namespace Inno.Editor.Core;

/// <summary>
/// Owns panel lifecycle and draw order.
/// </summary>
public sealed class EditorPanelRegistry
{
    private readonly List<IEditorPanel> m_panels = [];
    private readonly HashSet<string> m_ids = new(StringComparer.Ordinal);

    /// <summary>
    /// Gets panel count.
    /// </summary>
    public int count => m_panels.Count;

    /// <summary>
    /// Gets all registered panels in draw order.
    /// </summary>
    public IReadOnlyList<IEditorPanel> panels => m_panels;

    /// <summary>
    /// Registers one panel and attaches it to context.
    /// </summary>
    /// <param name="panel">Panel instance.</param>
    /// <param name="context">Shared editor context.</param>
    public void Register(IEditorPanel panel, EditorContext context)
    {
        ArgumentNullException.ThrowIfNull(panel);
        ArgumentNullException.ThrowIfNull(context);

        if (!m_ids.Add(panel.id))
            throw new InvalidOperationException($"Panel id already exists: '{panel.id}'.");

        m_panels.Add(panel);
        panel.OnAttach(context);
    }

    /// <summary>
    /// Unregisters all panels in reverse order.
    /// </summary>
    /// <param name="context">Shared editor context.</param>
    public void Clear(EditorContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        for (int i = m_panels.Count - 1; i >= 0; i--)
        {
            m_panels[i].OnDetach(context);
        }

        m_panels.Clear();
        m_ids.Clear();
    }
}
