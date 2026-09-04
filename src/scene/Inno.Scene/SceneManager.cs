using System;
using System.Collections.Generic;

namespace Inno.Scene;

/// <summary>
/// Provides Unity-style scene operations by resolving the world in the current runtime execution context.
/// </summary>
/// <remarks>
/// This façade owns no mutable scene state. Engine internals should depend on a concrete <see cref="SceneWorld"/>
/// and only script-facing code should use this type.
/// </remarks>
public static class SceneManager
{
    /// <summary>
    /// Gets the active scene in the current runtime session.
    /// </summary>
    public static GameScene? activeScene => SceneWorld.current.activeScene;

    /// <summary>
    /// Gets whether the current runtime session has an active scene.
    /// </summary>
    public static bool hasActiveScene => SceneWorld.current.hasActiveScene;

    /// <summary>
    /// Gets an immutable snapshot of scenes loaded by the current runtime session.
    /// </summary>
    public static IReadOnlyList<GameScene> loadedScenes => SceneWorld.current.loadedScenes;

    /// <summary>
    /// Gets the hierarchy index of a scene loaded by the current runtime session.
    /// </summary>
    /// <param name="scene">
    /// The loaded scene to locate.
    /// </param>
    /// <returns>
    /// The zero-based hierarchy index.
    /// </returns>
    public static int GetSceneIndex(GameScene scene) => SceneWorld.current.GetSceneIndex(scene);

    /// <summary>
    /// Moves a loaded scene to a hierarchy index without changing the active scene.
    /// </summary>
    /// <param name="scene">
    /// The loaded scene to move.
    /// </param>
    /// <param name="sceneIndex">
    /// The requested zero-based hierarchy index.
    /// </param>
    public static void SetSceneIndex(GameScene scene, int sceneIndex)
        => SceneWorld.current.SetSceneIndex(scene, sceneIndex);

    /// <summary>
    /// Replaces the current session scene set with one active scene.
    /// </summary>
    /// <param name="scene">
    /// The scene to load.
    /// </param>
    public static void LoadScene(GameScene scene) => SceneWorld.current.LoadScene(scene);

    /// <summary>
    /// Loads a scene alongside the current session scene set.
    /// </summary>
    /// <param name="scene">
    /// The scene to load additively.
    /// </param>
    /// <param name="makeActive">
    /// Whether the loaded scene becomes active.
    /// </param>
    public static void LoadSceneAdditive(GameScene scene, bool makeActive = true)
        => SceneWorld.current.LoadSceneAdditive(scene, makeActive);

    /// <summary>
    /// Creates and loads a new active scene in the current runtime session.
    /// </summary>
    /// <param name="name">
    /// The initial scene display name.
    /// </param>
    /// <returns>
    /// The newly created and loaded scene.
    /// </returns>
    public static GameScene LoadNewScene(string name = "Untitled Scene")
        => SceneWorld.current.LoadNewScene(name);

    /// <summary>
    /// Creates and additively loads a new scene in the current runtime session.
    /// </summary>
    /// <param name="name">
    /// The initial scene display name.
    /// </param>
    /// <param name="makeActive">
    /// Whether the new scene becomes active.
    /// </param>
    /// <returns>
    /// The newly created and loaded scene.
    /// </returns>
    public static GameScene LoadNewSceneAdditive(string name = "Untitled Scene", bool makeActive = true)
        => SceneWorld.current.LoadNewSceneAdditive(name, makeActive);

    /// <summary>
    /// Makes a loaded scene active in the current runtime session.
    /// </summary>
    /// <param name="scene">
    /// The loaded scene to activate.
    /// </param>
    public static void SetActiveScene(GameScene scene) => SceneWorld.current.SetActiveScene(scene);

    /// <summary>
    /// Moves a live object subtree into another scene owned by the current runtime session.
    /// </summary>
    /// <param name="gameObject">
    /// The live root object to move.
    /// </param>
    /// <param name="destination">
    /// The loaded destination scene.
    /// </param>
    public static void MoveGameObjectToScene(GameObject gameObject, GameScene destination)
        => SceneWorld.current.MoveGameObjectToScene(gameObject, destination);

    /// <summary>
    /// Unloads the active scene in the current runtime session when one exists.
    /// </summary>
    public static void UnloadActiveScene() => SceneWorld.current.UnloadActiveScene();

    /// <summary>
    /// Unloads one scene from the current runtime session.
    /// </summary>
    /// <param name="scene">
    /// The scene to unload.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the scene was loaded; otherwise, <see langword="false"/>.
    /// </returns>
    public static bool UnloadScene(GameScene scene) => SceneWorld.current.UnloadScene(scene);

    /// <summary>
    /// Unloads every scene from the current runtime session.
    /// </summary>
    public static void UnloadAllScenes() => SceneWorld.current.UnloadAllScenes();

    /// <summary>
    /// Advances fixed-step scene lifecycle callbacks in the current runtime session.
    /// </summary>
    /// <param name="fixedDeltaTime">
    /// The fixed simulation interval in seconds.
    /// </param>
    public static void FixedUpdate(float fixedDeltaTime) => SceneWorld.current.FixedUpdate(fixedDeltaTime);

    /// <summary>
    /// Advances variable-step scene lifecycle callbacks in the current runtime session.
    /// </summary>
    /// <param name="deltaTime">
    /// The elapsed frame time in seconds.
    /// </param>
    public static void Update(float deltaTime) => SceneWorld.current.Update(deltaTime);

    /// <summary>
    /// Advances late scene lifecycle callbacks in the current runtime session.
    /// </summary>
    /// <param name="deltaTime">
    /// The elapsed frame time in seconds.
    /// </param>
    public static void LateUpdate(float deltaTime) => SceneWorld.current.LateUpdate(deltaTime);
}
