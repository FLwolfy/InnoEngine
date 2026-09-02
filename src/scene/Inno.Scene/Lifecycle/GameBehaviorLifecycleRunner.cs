using System.Collections.Generic;

namespace Inno.Scene;

/// <summary>
/// Dispatches GameBehavior lifecycle messages from stable scene snapshots.
/// </summary>
internal sealed class GameBehaviorLifecycleRunner
{
    private readonly GameScene m_scene;

    internal GameBehaviorLifecycleRunner(GameScene scene)
    {
        m_scene = scene;
    }

    internal void FixedUpdate()
    {
        PrepareAll();
        IReadOnlyList<GameBehavior> behaviors = m_scene.GetComponents<GameBehavior>();
        for (int i = 0; i < behaviors.Count; i++)
        {
            if (!m_scene.canDispatch)
                break;
            GameBehavior behavior = behaviors[i];
            if (Prepare(behavior) && behavior.isActiveAndEnabled && m_scene.canDispatch)
                behavior.DispatchFixedUpdate();
        }
    }

    internal void Update()
    {
        PrepareAll();
        IReadOnlyList<GameBehavior> behaviors = m_scene.GetComponents<GameBehavior>();
        for (int i = 0; i < behaviors.Count; i++)
        {
            if (!m_scene.canDispatch)
                break;
            GameBehavior behavior = behaviors[i];
            if (!Prepare(behavior) || !behavior.isActiveAndEnabled || !m_scene.canDispatch)
                continue;
            if (!behavior.lifecycleStartCalled)
            {
                behavior.lifecycleStartCalled = true;
                behavior.DispatchStart();
                if (!m_scene.canDispatch || behavior.isDestroyed)
                    break;
            }
            behavior.DispatchUpdate();
        }
    }

    internal void LateUpdate()
    {
        PrepareAll();
        IReadOnlyList<GameBehavior> behaviors = m_scene.GetComponents<GameBehavior>();
        for (int i = 0; i < behaviors.Count; i++)
        {
            if (!m_scene.canDispatch)
                break;
            GameBehavior behavior = behaviors[i];
            if (Prepare(behavior) && behavior.isActiveAndEnabled && behavior.lifecycleStartCalled && m_scene.canDispatch)
                behavior.DispatchLateUpdate();
        }
    }

    internal void Refresh(GameBehavior behavior)
    {
        if (m_scene.canDispatch)
            _ = SceneLifecycle.Prepare(behavior, m_scene);
    }

    internal void RefreshAll()
    {
        if (m_scene.canDispatch)
            PrepareAll();
    }

    internal void Destroy(GameBehavior behavior)
        => SceneLifecycle.Destroy(behavior);

    private bool Prepare(GameBehavior behavior)
        => SceneLifecycle.Prepare(behavior, m_scene);

    private void PrepareAll()
    {
        IReadOnlyList<GameBehavior> behaviors = m_scene.GetComponents<GameBehavior>();
        for (int i = 0; i < behaviors.Count; i++)
        {
            if (!m_scene.canDispatch)
                break;
            _ = SceneLifecycle.Prepare(behaviors[i], m_scene);
        }
    }
}
