using System.Collections.Generic;

using Inno.Scene;

namespace Inno.Editor.Scene;

/// <summary>
/// Exposes the active Edit or Play scene presentation and the persistence operations available to it.
/// </summary>
public interface IEditorSceneWorkspace
{
    /// <summary>
    /// Gets the Edit scenes outside Play Mode or the isolated runtime copies while Play Mode is active.
    /// </summary>
    IReadOnlyList<GameScene> scenes { get; }

    /// <summary>
    /// Gets the active scene from the currently presented Edit or Play world.
    /// </summary>
    GameScene? activeScene { get; }

    /// <summary>
    /// Gets whether the currently presented scenes are authoring documents that may be persisted.
    /// </summary>
    bool canPersist { get; }

    /// <summary>
    /// Makes one presented scene active without changing scene order.
    /// </summary>
    /// <param name="scene">
    /// The loaded scene to activate.
    /// </param>
    /// <exception cref="System.ArgumentNullException">
    /// Thrown when <paramref name="scene"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="System.InvalidOperationException">
    /// Thrown when the scene is not loaded by this workspace.
    /// </exception>
    void SetActiveScene(GameScene scene);

    /// <summary>
    /// Gets whether an Edit scene contains unsaved serialized changes.
    /// </summary>
    /// <param name="scene">
    /// The scene to inspect.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when an Edit scene differs from its saved baseline; runtime copies always
    /// return <see langword="false"/> because they cannot be persisted.
    /// </returns>
    bool IsDirty(GameScene scene);

    /// <summary>
    /// Opens a scene asset additively as the active editor scene.
    /// </summary>
    /// <param name="relativePath">
    /// The source-relative scene asset path.
    /// </param>
    /// <returns>
    /// The existing loaded instance or newly loaded scene.
    /// </returns>
    /// <exception cref="System.InvalidOperationException">
    /// Thrown while Play Mode runtime copies are active or when the asset cannot be opened.
    /// </exception>
    GameScene Open(string relativePath);

    /// <summary>
    /// Saves a scene to its existing path or into a fallback directory.
    /// </summary>
    /// <param name="scene">
    /// The scene to save.
    /// </param>
    /// <param name="currentDirectory">
    /// The fallback asset directory for an unsaved scene.
    /// </param>
    /// <returns>
    /// The saved source-relative path.
    /// </returns>
    /// <exception cref="System.InvalidOperationException">
    /// Thrown while Play Mode runtime copies are active or when the asset cannot be persisted.
    /// </exception>
    string Save(GameScene scene, string currentDirectory);

    /// <summary>
    /// Saves a scene into the requested asset directory.
    /// </summary>
    /// <param name="scene">
    /// The scene to save.
    /// </param>
    /// <param name="currentDirectory">
    /// The target asset directory.
    /// </param>
    /// <returns>
    /// The saved source-relative path.
    /// </returns>
    /// <exception cref="System.InvalidOperationException">
    /// Thrown while Play Mode runtime copies are active or when the asset cannot be persisted.
    /// </exception>
    string SaveToDirectory(GameScene scene, string currentDirectory);

    /// <summary>
    /// Captures a game object subtree as a prefab in the requested directory.
    /// </summary>
    /// <param name="gameObject">
    /// The prefab root.
    /// </param>
    /// <param name="currentDirectory">
    /// The target asset directory.
    /// </param>
    /// <returns>
    /// The saved source-relative path.
    /// </returns>
    /// <exception cref="System.InvalidOperationException">
    /// Thrown while Play Mode runtime copies are active or when the asset cannot be persisted.
    /// </exception>
    string SavePrefab(GameObject gameObject, string currentDirectory);

    /// <summary>
    /// Tries to get the current source-relative asset path of a saved scene.
    /// </summary>
    /// <param name="scene">
    /// The scene whose path is requested.
    /// </param>
    /// <param name="relativePath">
    /// The saved path when available.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the scene is backed by a scene asset.
    /// </returns>
    bool TryGetSourcePath(GameScene scene, out string relativePath);
}
