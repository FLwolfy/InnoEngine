using Inno.Editor.Core.Panels;

namespace Inno.Editor.Interactions.Runtime;

/// <summary>Describes one active modal extension.</summary>
public sealed class EditorModalExtension
{
    internal EditorModalExtension(string id, string title, int order, EditorModal modal)
    {
        this.id = id;
        this.title = title;
        this.order = order;
        this.modal = modal;
    }

    /// <summary>Gets the stable modal identifier.</summary>
    public string id { get; }

    /// <summary>Gets the visible modal title.</summary>
    public string title { get; }

    /// <summary>Gets the stable modal ordering value.</summary>
    public int order { get; }

    /// <summary>Gets the active modal instance.</summary>
    public EditorModal modal { get; }
}
