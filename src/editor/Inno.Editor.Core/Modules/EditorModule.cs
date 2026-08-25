using System;

namespace Inno.Editor.Core;

/// <summary>
/// Owns optional shared state and lifecycle for one editor feature.
/// Simple panels and actions do not need a module.
/// </summary>
public abstract class EditorModule : IDisposable, IEditorWorkspaceState
{
    private bool m_disposed;

    /// <summary>
    /// Gets whether modules ordered after this module must defer their updates for the current frame.
    /// </summary>
    /// <remarks>
    /// A module may return <see langword="true"/> while completing a project-wide bootstrap or atomic
    /// transition that later modules must not observe partially. Panels and modals remain drawable.
    /// </remarks>
    public virtual bool blocksFollowingUpdates => false;

    /// <summary>
    /// Starts the module after the containing extension generation becomes active.
    /// </summary>
    /// <param name="context">The shared editor context for the active runtime.</param>
    public void Start(EditorContext context)
    {
        OnStart(context);
    }

    /// <summary>
    /// Updates the module once per editor frame before panels and modals are drawn.
    /// </summary>
    /// <param name="context">The shared editor context containing the current frame state.</param>
    public void Update(EditorContext context)
    {
        OnUpdate(context);
    }

    /// <summary>
    /// Stops the module before the containing extension generation is released.
    /// </summary>
    /// <param name="context">The shared editor context for the runtime being stopped.</param>
    public void Stop(EditorContext context)
    {
        OnStop(context);
    }

    /// <summary>
    /// Runs after the module generation becomes active and before its first update.
    /// </summary>
    /// <param name="context">The shared editor context for the active runtime.</param>
    protected virtual void OnStart(EditorContext context)
    {
    }

    /// <summary>
    /// Runs once per editor frame before views are drawn.
    /// </summary>
    /// <param name="context">The shared editor context containing the current frame state.</param>
    protected virtual void OnUpdate(EditorContext context)
    {
    }

    /// <summary>
    /// Runs before the module generation is released and its disposable instances are destroyed.
    /// </summary>
    /// <param name="context">The shared editor context for the runtime being stopped.</param>
    protected virtual void OnStop(EditorContext context)
    {
    }

    /// <summary>
    /// Gets the stable project-workspace identifier for this module, or <see langword="null"/>
    /// when the module does not persist workspace state.
    /// </summary>
    protected virtual string workspaceStateId => null!;

    /// <summary>
    /// Captures project-specific workspace state owned by this module.
    /// This hook is called only when <see cref="workspaceStateId"/> is non-empty.
    /// </summary>
    /// <param name="writer">
    /// The isolated writer assigned to this module.
    /// </param>
    protected virtual void CaptureWorkspaceState(EditorWorkspaceStateWriter writer)
    {
    }

    /// <summary>
    /// Restores project-specific workspace state owned by this module.
    /// This hook is called only when <see cref="workspaceStateId"/> is non-empty.
    /// </summary>
    /// <param name="reader">
    /// The isolated reader for this module. Its <see cref="EditorWorkspaceStateReader.hasState"/>
    /// property is <see langword="false"/> when no state was stored.
    /// </param>
    protected virtual void RestoreWorkspaceState(EditorWorkspaceStateReader reader)
    {
    }

    /// <summary>
    /// Releases resources owned by this module after it has stopped and left the active extension
    /// generation.
    /// </summary>
    protected virtual void OnDispose()
    {
    }

    string? IEditorWorkspaceState.workspaceStateId => workspaceStateId;

    void IEditorWorkspaceState.CaptureWorkspaceState(EditorWorkspaceStateWriter writer)
        => CaptureWorkspaceState(writer);

    void IEditorWorkspaceState.RestoreWorkspaceState(EditorWorkspaceStateReader reader)
        => RestoreWorkspaceState(reader);

    void IDisposable.Dispose()
    {
        if (m_disposed)
            return;
        m_disposed = true;
        OnDispose();
        GC.SuppressFinalize(this);
    }
}
