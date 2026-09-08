
using System;

using Inno.Scene;

namespace Inno.Editor.Panel.Hierarchy;

/// <summary>
/// Identifies a scene row or scene root hierarchy drop target.
/// </summary>
public sealed class HierarchySceneDropTarget
{
    /// <summary>
    /// Creates a hierarchy drop target representing one loaded scene row or root.
    /// </summary>
    /// <param name="scene">
    /// The loaded scene represented by the target.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="scene"/> is <see langword="null"/>.
    /// </exception>
    public HierarchySceneDropTarget(GameScene scene)
    {
        this.scene = scene ?? throw new ArgumentNullException(nameof(scene));
    }

    /// <summary>
    /// Gets the target scene.
    /// </summary>
    public GameScene scene { get; }
}
