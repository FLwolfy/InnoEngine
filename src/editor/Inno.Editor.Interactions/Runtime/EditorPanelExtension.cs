using System;

using Inno.Editor.Core;

namespace Inno.Editor.Interactions;

/// <summary>Describes one active dockable panel extension.</summary>
public sealed class EditorPanelExtension
{
    private readonly Action<Exception> m_quarantine;
    private readonly EditorPanel m_panel;

    internal EditorPanelExtension(
        EditorPanelId id,
        string title,
        int order,
        EditorPanel panel,
        Action<Exception> quarantine)
    {
        this.id = id;
        this.title = title;
        this.order = order;
        m_panel = panel;
        m_quarantine = quarantine;
    }

    /// <summary>Gets the stable panel identifier.</summary>
    public EditorPanelId id { get; }

    /// <summary>Gets the visible panel title.</summary>
    public string title { get; }

    /// <summary>Gets the stable panel ordering value.</summary>
    public int order { get; }

    /// <summary>Gets or sets whether this panel is open in the current extension generation.</summary>
    public bool isOpen
    {
        get => m_panel.isOpen;
        set => m_panel.isOpen = value;
    }

    internal bool useWindowPadding => m_panel.useWindowPadding;

    internal bool Draw(EditorContext context)
    {
        try
        {
            m_panel.Draw(context);
            return true;
        }
        catch (Exception exception)
        {
            m_panel.isOpen = false;
            m_quarantine(exception);
            return false;
        }
    }
}
