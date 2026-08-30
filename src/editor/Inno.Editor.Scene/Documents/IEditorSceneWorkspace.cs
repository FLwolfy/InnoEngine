using System.Collections.Generic;

using Inno.Engine.Scene;

namespace Inno.Editor.Scene;

/// <summary>
/// Exposes scene document queries and non-destructive file workflows without direct document mutation.
/// </summary>
public interface IEditorSceneWorkspace
{
    /// <summary>Gets all scenes currently available to editor features.</summary>
    IReadOnlyList<GameScene> scenes { get; }

    /// <summary>Gets the active scene, or <see langword="null"/> when no scene is active.</summary>
    GameScene? activeScene { get; }

    /// <summary>Gets whether scene documents may currently be persisted to project assets.</summary>
    bool canPersist { get; }

    /// <summary>Gets whether a scene contains unsaved serialized changes.</summary>
    /// <param name="scene">The scene to inspect.</param>
    /// <returns><see langword="true"/> when the scene differs from its saved baseline.</returns>
    bool IsDirty(GameScene scene);

    /// <summary>Opens a scene asset additively as the active editor scene.</summary>
    /// <param name="relativePath">The source-relative scene asset path.</param>
    /// <returns>The existing loaded instance or newly loaded scene.</returns>
    GameScene Open(string relativePath);

    /// <summary>Saves a scene to its existing path or into a fallback directory.</summary>
    /// <param name="scene">The scene to save.</param>
    /// <param name="currentDirectory">The fallback asset directory for an unsaved scene.</param>
    /// <returns>The saved source-relative path.</returns>
    /// <exception cref="System.InvalidOperationException">
    /// Thrown while Play Mode runtime copies are active or when the asset cannot be persisted.
    /// </exception>
    string Save(GameScene scene, string currentDirectory);

    /// <summary>Saves a scene into the requested asset directory.</summary>
    /// <param name="scene">The scene to save.</param>
    /// <param name="currentDirectory">The target asset directory.</param>
    /// <returns>The saved source-relative path.</returns>
    /// <exception cref="System.InvalidOperationException">
    /// Thrown while Play Mode runtime copies are active or when the asset cannot be persisted.
    /// </exception>
    string SaveToDirectory(GameScene scene, string currentDirectory);

    /// <summary>Captures a game object subtree as a prefab in the requested directory.</summary>
    /// <param name="gameObject">The prefab root.</param>
    /// <param name="currentDirectory">The target asset directory.</param>
    /// <returns>The saved source-relative path.</returns>
    /// <exception cref="System.InvalidOperationException">
    /// Thrown while Play Mode runtime copies are active or when the asset cannot be persisted.
    /// </exception>
    string SavePrefab(GameObject gameObject, string currentDirectory);

    /// <summary>Tries to get the current source-relative asset path of a saved scene.</summary>
    /// <param name="scene">The scene whose path is requested.</param>
    /// <param name="relativePath">The saved path when available.</param>
    /// <returns><see langword="true"/> when the scene is backed by a scene asset.</returns>
    bool TryGetSourcePath(GameScene scene, out string relativePath);
}
