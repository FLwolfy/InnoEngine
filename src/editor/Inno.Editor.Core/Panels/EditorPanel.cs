
namespace Inno.Editor.Core;

/// <summary>
/// Base class for editor panel implementations.
/// </summary>
public abstract class EditorPanel : IEditorWorkspaceState
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
    /// Gets the stable project-workspace identifier for this panel, or <see langword="null"/>
    /// when the panel does not persist workspace state.
    /// </summary>
    protected virtual string workspaceStateId => null!;

    /// <summary>
    /// Captures project-specific workspace state owned by this panel.
    /// This hook is called only when <see cref="workspaceStateId"/> is non-empty.
    /// </summary>
    /// <param name="writer">
    /// The isolated writer assigned to this panel.
    /// </param>
    protected virtual void CaptureWorkspaceState(EditorWorkspaceStateWriter writer)
    {
    }

    /// <summary>
    /// Restores project-specific workspace state owned by this panel.
    /// This hook is called only when <see cref="workspaceStateId"/> is non-empty.
    /// </summary>
    /// <param name="reader">
    /// The isolated reader for this panel. Its <see cref="EditorWorkspaceStateReader.hasState"/>
    /// property is <see langword="false"/> when no state was stored.
    /// </param>
    protected virtual void RestoreWorkspaceState(EditorWorkspaceStateReader reader)
    {
    }

    string? IEditorWorkspaceState.workspaceStateId => workspaceStateId;

    void IEditorWorkspaceState.CaptureWorkspaceState(EditorWorkspaceStateWriter writer)
        => CaptureWorkspaceState(writer);

    void IEditorWorkspaceState.RestoreWorkspaceState(EditorWorkspaceStateReader reader)
        => RestoreWorkspaceState(reader);
}
