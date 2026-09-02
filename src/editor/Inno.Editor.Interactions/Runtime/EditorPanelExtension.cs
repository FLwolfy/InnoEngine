using System;

using Inno.Editor.Core;

namespace Inno.Editor.Interactions;

/// <summary>
/// Describes one active dockable panel extension.
/// </summary>
public sealed class EditorPanelExtension
{
    private readonly Action<Exception> m_quarantine;
    private readonly EditorPanel m_panel;

    internal EditorPanelExtension(
        string id,
        string title,
        int order,
        EditorPanel panel,
        Action<Exception> quarantine)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        this.id = id;
        this.title = title;
        this.order = order;
        m_panel = panel;
        m_quarantine = quarantine;
    }

    /// <summary>
    /// Gets the stable panel identifier.
    /// </summary>
    public string id { get; }

    /// <summary>
    /// Gets the visible panel title.
    /// </summary>
    public string title { get; }

    /// <summary>
    /// Gets the stable panel ordering value.
    /// </summary>
    public int order { get; }

    /// <summary>
    /// Gets or sets whether this panel is open in the current extension generation.
    /// </summary>
    public bool isOpen
    {
        get => m_panel.isOpen;
        set => m_panel.isOpen = value;
    }

    /// <summary>
    /// Safely reads the panel window-presentation policy through the active extension boundary.
    /// </summary>
    /// <param name="useWindowPadding">
    /// The requested padding policy, or the safe default when the panel is quarantined.
    /// </param>
    /// <param name="allowScrolling">
    /// The requested scrolling policy, or the safe default when the panel is quarantined.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the policy was read without quarantining the panel.
    /// </returns>
    public bool TryGetWindowPresentation(
        out bool useWindowPadding,
        out bool allowScrolling)
    {
        try
        {
            useWindowPadding = m_panel.useWindowPadding;
            allowScrolling = m_panel.allowScrolling;
            return true;
        }
        catch (Exception exception)
        {
            useWindowPadding = true;
            allowScrolling = true;
            m_panel.isOpen = false;
            m_quarantine(exception);
            return false;
        }
    }

    /// <summary>
    /// Safely draws the panel body and quarantines a failing extension instance.
    /// </summary>
    /// <param name="context">
    /// The active editor context supplied by the presentation backend.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the panel completed drawing without being quarantined.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="context"/> is <see langword="null"/>.
    /// </exception>
    public bool Draw(EditorContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
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
