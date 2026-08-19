using Inno.Editor.Scene.DragDrop;

using System;

using Inno.Engine.Scene;

namespace Inno.Editor.Scene.DragDrop;

/// <summary>Identifies a game object hierarchy drop target.</summary>
public sealed class HierarchyObjectDropTarget
{
    /// <summary>Creates a game object hierarchy target.</summary>
    public HierarchyObjectDropTarget(GameObject gameObject)
    {
        this.gameObject = gameObject ?? throw new ArgumentNullException(nameof(gameObject));
    }

    /// <summary>Gets the target game object.</summary>
    public GameObject gameObject { get; }
}
