using Inno.Editor.Core;

namespace Inno.Editor.Core.Panels;

/// <summary>
/// Base class for editor panel implementations.
/// </summary>
public abstract class EditorPanel
{
    /// <summary>
    /// Gets or sets whether panel is visible.
    /// </summary>
    public bool isOpen { get; set; } = true;

    /// <summary>Attaches the panel to an active editor context.</summary>
    public void Attach(EditorContext context) => OnAttach(context);

    /// <summary>Detaches the panel from an active editor context.</summary>
    public void Detach(EditorContext context) => OnDetach(context);

    /// <inheritdoc />
    protected virtual void OnAttach(EditorContext context)
    {
    }

    /// <inheritdoc />
    protected virtual void OnDetach(EditorContext context)
    {
    }

    /// <inheritdoc />
    public abstract void Draw(EditorContext context);
}
