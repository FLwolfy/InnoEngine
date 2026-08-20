using Inno.Editor.Scene.DragDrop;

using System;

using Inno.Engine.Scene;

namespace Inno.Editor.Scene.DragDrop;

/// <summary>Identifies a game object hierarchy drop target.</summary>
public sealed class HierarchyObjectDropTarget
{
    /// <summary>
    /// Creates a hierarchy drop target representing one live game object row.
    /// </summary>
    /// <param name="gameObject">The game object represented by the target row.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="gameObject"/> is <see langword="null"/>.</exception>
    public HierarchyObjectDropTarget(GameObject gameObject)
    {
        this.gameObject = gameObject ?? throw new ArgumentNullException(nameof(gameObject));
    }

    /// <summary>Gets the target game object.</summary>
    public GameObject gameObject { get; }
}
