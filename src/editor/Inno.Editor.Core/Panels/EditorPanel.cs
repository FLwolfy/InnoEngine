
namespace Inno.Editor.Core;

/// <summary>
/// Base class for editor panel implementations.
/// </summary>
public abstract class EditorPanel
{
    /// <summary>
    /// Gets whether the presentation backend should inset this panel body by its standard
    /// window padding.
    /// </summary>
    public virtual bool useWindowPadding => true;

    /// <summary>
    /// Gets or sets whether panel is visible.
    /// </summary>
    public bool isOpen { get; set; } = true;

    /// <summary>
    /// Attaches the panel after its extension generation becomes active.
    /// </summary>
    /// <param name="context">The shared editor context for the active runtime.</param>
    public void Attach(EditorContext context) => OnAttach(context);

    /// <summary>
    /// Detaches the panel before its extension generation is released.
    /// </summary>
    /// <param name="context">The shared editor context for the runtime being detached.</param>
    public void Detach(EditorContext context) => OnDetach(context);

    /// <summary>
    /// Runs after the panel is attached to an active extension generation.
    /// </summary>
    /// <param name="context">The shared editor context for the active runtime.</param>
    protected virtual void OnAttach(EditorContext context)
    {
    }

    /// <summary>
    /// Runs before the panel is detached from its active extension generation.
    /// </summary>
    /// <param name="context">The shared editor context for the runtime being detached.</param>
    protected virtual void OnDetach(EditorContext context)
    {
    }

    /// <summary>
    /// Draws the complete dockable contents of the panel for the current frame.
    /// </summary>
    /// <param name="context">The shared editor context containing current selection, focus, and frame state.</param>
    public abstract void Draw(EditorContext context);
}
