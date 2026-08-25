using System;
using System.Numerics;

using Inno.Editor.Core;

namespace Inno.Editor.Interactions;

/// <summary>Describes one active modal extension.</summary>
public sealed class EditorModalExtension
{
    private readonly Action<Exception> m_quarantine;
    private readonly EditorModal m_modal;
    private bool m_isQuarantined;

    internal EditorModalExtension(
        string id,
        string title,
        int order,
        EditorModal modal,
        Action<Exception> quarantine)
    {
        this.id = id;
        this.title = title;
        this.order = order;
        m_modal = modal;
        m_quarantine = quarantine;
    }

    /// <summary>Gets the stable modal identifier.</summary>
    public string id { get; }

    /// <summary>Gets the visible modal title.</summary>
    public string title { get; }

    /// <summary>Gets the stable modal ordering value.</summary>
    public int order { get; }

    internal bool TryGetPresentation(out Presentation presentation)
    {
        if (m_isQuarantined)
        {
            presentation = default;
            return false;
        }
        try
        {
            presentation = new Presentation(
                m_modal.isVisible,
                m_modal.blocksInteraction,
                m_modal.canMove,
                m_modal.canResize,
                m_modal.initialSize,
                m_modal.minimumSize);
            return true;
        }
        catch (Exception exception)
        {
            presentation = default;
            m_isQuarantined = true;
            m_quarantine(exception);
            return false;
        }
    }

    internal bool Draw(EditorContext context)
    {
        if (m_isQuarantined)
            return false;
        try
        {
            m_modal.Draw(context);
            return true;
        }
        catch (Exception exception)
        {
            m_isQuarantined = true;
            m_quarantine(exception);
            return false;
        }
    }

    internal readonly record struct Presentation(
        bool isVisible,
        bool blocksInteraction,
        bool canMove,
        bool canResize,
        Vector2 initialSize,
        Vector2 minimumSize);
}
