using System;

using Inno.Core.Scripting;

namespace Inno.Editor.Core;

/// <summary>
/// Owns optional shared state and lifecycle for one editor feature.
/// Simple panels and actions do not need a module.
/// </summary>
public abstract class EditorModule : IDisposable
{
    private bool m_started;
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
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="context"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ObjectDisposedException">
    /// Thrown after the module has been disposed.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the module is already started.
    /// </exception>
    [ScriptingApiIgnore]
    public void Start(EditorContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        ObjectDisposedException.ThrowIf(m_disposed, this);
        if (m_started)
            throw new InvalidOperationException($"Editor module '{GetType().FullName}' is already started.");
        OnStart(context);
        m_started = true;
    }

    /// <summary>
    /// Updates the module once per editor frame before panels and modals are drawn.
    /// </summary>
    /// <param name="context">The shared editor context containing the current frame state.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="context"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ObjectDisposedException">
    /// Thrown after the module has been disposed.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the module is not started.
    /// </exception>
    [ScriptingApiIgnore]
    public void Update(EditorContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        ObjectDisposedException.ThrowIf(m_disposed, this);
        if (!m_started)
            throw new InvalidOperationException($"Editor module '{GetType().FullName}' is not started.");
        OnUpdate(context);
    }

    /// <summary>
    /// Stops the module before the containing extension generation is released.
    /// </summary>
    /// <param name="context">The shared editor context for the runtime being stopped.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="context"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ObjectDisposedException">
    /// Thrown after the module has been disposed.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the module is not started.
    /// </exception>
    [ScriptingApiIgnore]
    public void Stop(EditorContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        ObjectDisposedException.ThrowIf(m_disposed, this);
        if (!m_started)
            throw new InvalidOperationException($"Editor module '{GetType().FullName}' is not started.");
        OnStop(context);
        m_started = false;
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
    /// Captures readable project state owned by this module.
    /// </summary>
    /// <remarks>
    /// Overriding this method opts the module into project-state IO. Modules that keep the base
    /// implementation are never registered with the persistence coordinator and therefore perform
    /// no state reads or writes.
    /// </remarks>
    /// <param name="state">
    /// The writable parameter that receives the complete readable state for this module.
    /// </param>
    protected virtual void Capture(EditorState state)
    {
    }

    /// <summary>
    /// Restores readable project state owned by this module.
    /// </summary>
    /// <remarks>
    /// This method is called only when <see cref="Capture"/> is overridden. It runs once after the
    /// module is started and before the module is allowed to capture replacement state.
    /// </remarks>
    /// <param name="state">
    /// The read-only state parameter for this module. Missing or incompatible values return the
    /// fallback supplied to <see cref="EditorState.Get{T}(string, T)"/>.
    /// </param>
    protected virtual void Restore(EditorState state)
    {
    }

    /// <summary>
    /// Releases resources owned by this module after it has stopped and left the active extension
    /// generation.
    /// </summary>
    protected virtual void OnDispose()
    {
    }

    void IDisposable.Dispose()
    {
        if (m_disposed)
            return;
        m_disposed = true;
        OnDispose();
        GC.SuppressFinalize(this);
    }
}
