
using System;

using Inno.Engine.Scene;

namespace Inno.Editor.Panel.Inspector;

/// <summary>Identifies a component together with its owning game object.</summary>
public sealed class ComponentEditorTarget
{
    /// <summary>
    /// Creates an inspector action target that keeps a component paired with its owning game object.
    /// </summary>
    /// <param name="gameObject">The live game object that owns the component.</param>
    /// <param name="component">The component operated on by the inspector action.</param>
    /// <exception cref="ArgumentNullException">Thrown when either argument is <see langword="null"/>.</exception>
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
