using Inno.Editor.Scene.DragDrop;

using System;

using Inno.Engine.Scene;

namespace Inno.Editor.Scene.DragDrop;

/// <summary>Identifies a scene row or scene root hierarchy drop target.</summary>
public sealed class HierarchySceneDropTarget
{
    /// <summary>Creates a scene hierarchy target.</summary>
    public HierarchySceneDropTarget(GameScene scene)
    {
        this.scene = scene ?? throw new ArgumentNullException(nameof(scene));
    }

    /// <summary>Gets the target scene.</summary>
    public GameScene scene { get; }
}
