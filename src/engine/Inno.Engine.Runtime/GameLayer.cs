using Inno.Core.Framework;
using Inno.Engine.Assets;
using Inno.Engine.Scene;

namespace Inno.Engine.Runtime;

/// <summary>
/// Shell layer that advances the active game scene.
/// </summary>
public sealed class GameLayer : Layer
{
    private readonly GameScene? m_startupScene;
    private readonly bool m_unloadSceneOnDetach;

    static GameLayer()
    {
        _ = typeof(SceneAsset).Assembly;
        _ = typeof(PrefabAsset).Assembly;
    }

    /// <summary>
    /// Creates a game layer. If no startup scene is provided, an empty scene is created on attach.
    /// </summary>
    /// <param name="startupScene">Optional startup scene.</param>
    /// <param name="unloadSceneOnDetach">Whether to unload the active scene when this layer detaches.</param>
    public GameLayer(GameScene? startupScene = null, bool unloadSceneOnDetach = true)
        : base("GameLayer")
    {
        m_startupScene = startupScene;
        m_unloadSceneOnDetach = unloadSceneOnDetach;
    }

    /// <summary>
    /// Called when the layer is attached to the shell.
    /// </summary>
    public override void OnAttach()
    {
        if (SceneManager.hasActiveScene)
        {
            return;
        }

        SceneManager.LoadScene(m_startupScene ?? new GameScene());
    }

    /// <summary>
    /// Advances the active scene fixed update.
    /// </summary>
    /// <param name="fixedDeltaTime">Fixed timestep in seconds.</param>
    public override void OnFixedUpdate(float fixedDeltaTime)
    {
        SceneManager.FixedUpdate(fixedDeltaTime);
    }

    /// <summary>
    /// Advances the active scene update.
    /// </summary>
    /// <param name="deltaTime">Frame delta time in seconds.</param>
    public override void OnUpdate(float deltaTime)
    {
        SceneManager.Update(deltaTime);
    }

    /// <summary>
    /// Advances the active scene late update.
    /// </summary>
    /// <param name="deltaTime">Frame delta time in seconds.</param>
    public override void OnLateUpdate(float deltaTime)
    {
        SceneManager.LateUpdate(deltaTime);
    }

    /// <summary>
    /// Called when the layer is detached from the shell.
    /// </summary>
    public override void OnDetach()
    {
        if (m_unloadSceneOnDetach)
        {
            SceneManager.UnloadActiveScene();
        }
    }
}
