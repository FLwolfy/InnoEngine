namespace Inno.Editor.Core;

/// <summary>
/// Base class for editor panel implementations.
/// </summary>
public abstract class EditorPanel(string id, string title)
{
    /// <summary>
    /// Gets panel stable identifier.
    /// </summary>
    public string id { get; } = id;

    /// <summary>
    /// Gets panel display title.
    /// </summary>
    public string title { get; } = title;

    /// <summary>
    /// Gets or sets whether panel is visible.
    /// </summary>
    public bool isOpen { get; set; } = true;

    /// <inheritdoc />
    public virtual void OnAttach(EditorContext context)
    {
    }

    /// <inheritdoc />
    public virtual void OnDetach(EditorContext context)
    {
    }

    /// <inheritdoc />
    public abstract void OnRender(EditorContext context);
}
