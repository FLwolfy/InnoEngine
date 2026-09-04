using System;

using Inno.Scene;

namespace Inno.Editor.Scene;

/// <summary>
/// Exposes world-owned hierarchy operations inside one atomic scene history mutation.
/// </summary>
public sealed class SceneHierarchyEdit
{
    private readonly SceneWorld m_world;

    internal SceneHierarchyEdit(SceneWorld world)
    {
        m_world = world;
    }

    /// <summary>
    /// Moves a live GameObject subtree into another scene owned by the current editor world.
    /// </summary>
    /// <param name="gameObject">
    /// The live root object to move.
    /// </param>
    /// <param name="destination">
    /// The loaded destination scene.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when an argument is <see langword="null"/>.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when either scene is not loaded by the editor world.
    /// </exception>
    public void MoveToScene(GameObject gameObject, GameScene destination)
        => m_world.MoveGameObjectToScene(gameObject, destination);
}
