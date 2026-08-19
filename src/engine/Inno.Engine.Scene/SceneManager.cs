using System;
using System.Collections.Generic;

namespace Inno.Engine.Scene;

/// <summary>
/// Manages loaded scene lifecycles for the current runtime.
/// </summary>
public static class SceneManager
{
    private static readonly List<GameScene> s_loadedScenes = [];
    private static GameScene? s_activeScene;

    /// <summary>
    /// Gets the currently loaded scene.
    /// </summary>
    public static GameScene? activeScene => s_activeScene;

    /// <summary>
    /// Gets whether a scene is currently loaded.
    /// </summary>
    public static bool hasActiveScene => s_activeScene is not null;

    /// <summary>
    /// Gets loaded scenes in hierarchy display order.
    /// </summary>
    public static IReadOnlyList<GameScene> loadedScenes => s_loadedScenes.ToArray();

    /// <summary>
    /// Gets the hierarchy index of a loaded scene.
    /// </summary>
    /// <param name="scene">Loaded scene to locate.</param>
    /// <returns>The zero-based scene index.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the scene is not loaded.</exception>
    public static int GetSceneIndex(GameScene scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        int index = s_loadedScenes.IndexOf(scene);
        return index >= 0
            ? index
            : throw new InvalidOperationException("Only a loaded scene has a hierarchy index.");
    }

    /// <summary>
    /// Moves a loaded scene to a hierarchy index without changing the active scene.
    /// </summary>
    /// <param name="scene">Loaded scene to move.</param>
    /// <param name="sceneIndex">Requested zero-based hierarchy index.</param>
    /// <exception cref="InvalidOperationException">Thrown when the scene is not loaded.</exception>
    public static void SetSceneIndex(GameScene scene, int sceneIndex)
    {
        ArgumentNullException.ThrowIfNull(scene);
        int currentIndex = s_loadedScenes.IndexOf(scene);
        if (currentIndex < 0)
            throw new InvalidOperationException("Only a loaded scene can be reordered.");
        int targetIndex = Math.Clamp(sceneIndex, 0, s_loadedScenes.Count - 1);
        if (currentIndex == targetIndex)
            return;
        s_loadedScenes.RemoveAt(currentIndex);
        s_loadedScenes.Insert(targetIndex, scene);
    }

    /// <summary>
    /// Loads a scene as the active scene, unloading the previous scene first.
    /// </summary>
    /// <param name="scene">Scene to load.</param>
    public static void LoadScene(GameScene scene)
    {
        ArgumentNullException.ThrowIfNull(scene);

        if (ReferenceEquals(s_activeScene, scene) && s_loadedScenes.Count == 1)
        {
            return;
        }

        UnloadAllScenes();
        s_loadedScenes.Add(scene);
        s_activeScene = scene;
        scene.Load();
    }

    /// <summary>
    /// Loads a scene alongside the currently loaded scenes.
    /// </summary>
    /// <param name="scene">Scene to load additively.</param>
    /// <param name="makeActive">Whether the loaded scene becomes active.</param>
    public static void LoadSceneAdditive(GameScene scene, bool makeActive = true)
    {
        ArgumentNullException.ThrowIfNull(scene);
        if (!s_loadedScenes.Contains(scene))
        {
            s_loadedScenes.Add(scene);
            scene.Load();
        }

        if (makeActive || s_activeScene is null)
        {
            s_activeScene = scene;
        }
    }

    /// <summary>
    /// Creates and loads a new scene as the active scene.
    /// </summary>
    /// <param name="name">Scene display name.</param>
    /// <returns>The loaded scene.</returns>
    public static GameScene LoadNewScene(string name = "Untitled Scene")
    {
        var scene = new GameScene(name);
        LoadScene(scene);
        return scene;
    }

    /// <summary>
    /// Creates and loads a scene alongside the currently loaded scenes.
    /// </summary>
    /// <param name="name">Scene display name.</param>
    /// <param name="makeActive">Whether the new scene becomes active.</param>
    /// <returns>The newly loaded scene.</returns>
    public static GameScene LoadNewSceneAdditive(string name = "Untitled Scene", bool makeActive = true)
    {
        var scene = new GameScene(name);
        LoadSceneAdditive(scene, makeActive);
        return scene;
    }

    /// <summary>
    /// Makes a loaded scene active without changing the loaded scene set.
    /// </summary>
    /// <param name="scene">Loaded scene to activate.</param>
    /// <exception cref="InvalidOperationException">Thrown when the scene is not loaded.</exception>
    public static void SetActiveScene(GameScene scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        if (!s_loadedScenes.Contains(scene))
        {
            throw new InvalidOperationException("Only a loaded scene can become active.");
        }

        s_activeScene = scene;
    }

    /// <summary>
    /// Unloads the active scene if one is loaded.
    /// </summary>
    public static void UnloadActiveScene()
    {
        if (s_activeScene is GameScene scene)
        {
            UnloadScene(scene);
        }
    }

    /// <summary>
    /// Unloads a specific scene and selects another loaded scene when necessary.
    /// </summary>
    /// <param name="scene">Scene to unload.</param>
    /// <returns><see langword="true"/> when the scene was loaded.</returns>
    public static bool UnloadScene(GameScene scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        if (!s_loadedScenes.Remove(scene))
        {
            return false;
        }

        try
        {
            scene.Unload();
        }
        finally
        {
            if (ReferenceEquals(s_activeScene, scene))
                s_activeScene = s_loadedScenes.Count > 0 ? s_loadedScenes[^1] : null;
        }

        return true;
    }

    /// <summary>
    /// Unloads every loaded scene.
    /// </summary>
    public static void UnloadAllScenes()
    {
        Exception? firstException = null;
        for (int i = s_loadedScenes.Count - 1; i >= 0; i--)
        {
            try
            {
                s_loadedScenes[i].Unload();
            }
            catch (Exception exception)
            {
                firstException ??= exception;
            }
        }

        s_loadedScenes.Clear();
        s_activeScene = null;
        if (firstException is not null)
            throw new InvalidOperationException("One or more scenes failed while unloading.", firstException);
    }

    /// <summary>
    /// Advances every loaded scene fixed update.
    /// </summary>
    /// <param name="fixedDeltaTime">Fixed timestep in seconds.</param>
    public static void FixedUpdate(float fixedDeltaTime)
    {
        GameScene[] scenes = [.. s_loadedScenes];
        for (int i = 0; i < scenes.Length; i++)
        {
            scenes[i].FixedUpdate(fixedDeltaTime);
        }
    }

    /// <summary>
    /// Advances every loaded scene update.
    /// </summary>
    /// <param name="deltaTime">Frame delta time in seconds.</param>
    public static void Update(float deltaTime)
    {
        GameScene[] scenes = [.. s_loadedScenes];
        for (int i = 0; i < scenes.Length; i++)
        {
            scenes[i].Update(deltaTime);
        }
    }

    /// <summary>
    /// Advances every loaded scene late update.
    /// </summary>
    /// <param name="deltaTime">Frame delta time in seconds.</param>
    public static void LateUpdate(float deltaTime)
    {
        GameScene[] scenes = [.. s_loadedScenes];
        for (int i = 0; i < scenes.Length; i++)
        {
            scenes[i].LateUpdate(deltaTime);
        }
    }
}
