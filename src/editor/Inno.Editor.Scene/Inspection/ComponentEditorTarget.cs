using Inno.Editor.Scene.Inspection;

using System;

using Inno.Engine.Scene;

namespace Inno.Editor.Scene.Inspection;

/// <summary>Identifies a component together with its owning game object.</summary>
public sealed class ComponentEditorTarget
{
    /// <summary>Creates a component editor target.</summary>
    public ComponentEditorTarget(GameObject gameObject, GameComponent component)
    {
        this.gameObject = gameObject ?? throw new ArgumentNullException(nameof(gameObject));
        this.component = component ?? throw new ArgumentNullException(nameof(component));
    }

    /// <summary>Gets the owning game object.</summary>
    public GameObject gameObject { get; }

    /// <summary>Gets the component.</summary>
    public GameComponent component { get; }
}
