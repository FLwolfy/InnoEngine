namespace Inno.Editor.Core;

/// <summary>
/// Owns optional shared state and lifecycle for one editor feature.
/// Simple panels and actions do not need a module.
/// </summary>
public abstract class EditorModule
{
    /// <summary>Starts the module after its generation becomes active.</summary>
    public void Start(EditorContext context)
    {
        OnStart(context);
    }

    /// <summary>Updates the module once per editor frame before views are drawn.</summary>
    public void Update(EditorContext context)
    {
        OnUpdate(context);
    }

    /// <summary>Stops the module before its generation is released.</summary>
    public void Stop(EditorContext context)
    {
        OnStop(context);
    }

    /// <summary>Runs after the module generation becomes active.</summary>
    protected virtual void OnStart(EditorContext context)
    {
    }

    /// <summary>Runs once per editor frame before views are drawn.</summary>
    protected virtual void OnUpdate(EditorContext context)
    {
    }

    /// <summary>Runs before the module generation is released.</summary>
    protected virtual void OnStop(EditorContext context)
    {
    }
}
