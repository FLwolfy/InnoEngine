using System;

namespace Inno.Engine.Scene;

/// <summary>
/// Global scene lifecycle manager for the current runtime.
/// </summary>
public static class SceneManager
{
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
    /// Loads a scene as the active scene, unloading the previous scene first.
    /// </summary>
    /// <param name="scene">Scene to load.</param>
    public static void LoadScene(GameScene scene)
    {
        ArgumentNullException.ThrowIfNull(scene);

        if (ReferenceEquals(s_activeScene, scene))
        {
            return;
        }

        UnloadActiveScene();
        s_activeScene = scene;
        scene.Load();
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
    /// Unloads the active scene if one is loaded.
    /// </summary>
    public static void UnloadActiveScene()
    {
        GameScene? scene = s_activeScene;
        if (scene is null)
        {
            return;
        }

        s_activeScene = null;
        scene.Unload();
    }

    /// <summary>
    /// Advances the active scene fixed update.
    /// </summary>
    /// <param name="fixedDeltaTime">Fixed timestep in seconds.</param>
    public static void FixedUpdate(float fixedDeltaTime)
    {
        s_activeScene?.FixedUpdate(fixedDeltaTime);
    }

    /// <summary>
    /// Advances the active scene update.
    /// </summary>
    /// <param name="deltaTime">Frame delta time in seconds.</param>
    public static void Update(float deltaTime)
    {
        s_activeScene?.Update(deltaTime);
    }

    /// <summary>
    /// Advances the active scene late update.
    /// </summary>
    /// <param name="deltaTime">Frame delta time in seconds.</param>
    public static void LateUpdate(float deltaTime)
    {
        s_activeScene?.LateUpdate(deltaTime);
    }
}
