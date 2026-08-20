namespace Inno.Editor.Core;

/// <summary>
/// Owns optional shared state and lifecycle for one editor feature.
/// Simple panels and actions do not need a module.
/// </summary>
public abstract class EditorModule
{
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
}
