
using System;

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
    /// Draws the complete dockable contents of the panel for the current frame.
    /// </summary>
    /// <param name="context">
    /// The shared editor context containing current selection, focus, and frame state.
    /// </param>
    public abstract void Draw(EditorContext context);

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
    /// Captures readable project state owned by this panel.
    /// </summary>
    /// <remarks>
    /// Overriding this method opts the panel into project-state IO. Panels that keep the base
    /// implementation are never registered with the persistence coordinator and therefore perform
    /// no state reads or writes. Panel visibility is persisted separately for every registered panel.
    /// </remarks>
    /// <param name="state">
    /// The writable parameter that receives the complete readable state for this panel.
    /// </param>
    protected virtual void Capture(EditorState state)
    {
    }

    /// <summary>
    /// Restores readable project state owned by this panel.
    /// </summary>
    /// <remarks>
    /// This method is called only when <see cref="Capture"/> is overridden. It runs once after the
    /// panel is attached and before the panel is allowed to capture replacement state.
    /// </remarks>
    /// <param name="state">
    /// The read-only state parameter for this panel. Missing or incompatible values return the
    /// fallback supplied to <see cref="EditorState.Get{T}(string, T)"/>.
    /// </param>
    protected virtual void Restore(EditorState state)
    {
    }
}
