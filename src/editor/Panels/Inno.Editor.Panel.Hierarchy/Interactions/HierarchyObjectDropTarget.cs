
using System;

using Inno.Scene;

namespace Inno.Editor.Panel.Hierarchy;

/// <summary>
/// Identifies a game object hierarchy drop target.
/// </summary>
public sealed class HierarchyObjectDropTarget
{
    /// <summary>
    /// Creates a hierarchy drop target representing one live game object row.
    /// </summary>
    /// <param name="gameObject">
    /// The game object represented by the target row.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="gameObject"/> is <see langword="null"/>.
    /// </exception>
    public HierarchyObjectDropTarget(GameObject gameObject)
    {
        this.gameObject = gameObject ?? throw new ArgumentNullException(nameof(gameObject));
    }

    /// <summary>
    /// Gets the target game object.
    /// </summary>
    public GameObject gameObject { get; }
}
