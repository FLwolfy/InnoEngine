using System.Collections.Generic;

namespace Inno.Engine.Scene;

/// <summary>Dispatches behavior lifecycle messages from stable snapshots.</summary>
internal sealed class BehaviorLifecycleRunner
{
    private readonly GameScene m_scene;

    internal BehaviorLifecycleRunner(GameScene scene)
    {
        m_scene = scene;
    }

    internal void FixedUpdate(float fixedDeltaTime)
    {
        IReadOnlyList<GameBehavior> behaviors = m_scene.GetComponents<GameBehavior>();
        for (int i = 0; i < behaviors.Count; i++)
        {
            if (!m_scene.canDispatch)
                break;
            GameBehavior behavior = behaviors[i];
            if (Prepare(behavior) && behavior.isActiveAndEnabled && m_scene.canDispatch)
                behavior.DispatchFixedUpdate(fixedDeltaTime);
        }
    }

    internal void Update(float deltaTime)
    {
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
            behavior.DispatchUpdate(deltaTime);
        }
    }

    internal void LateUpdate(float deltaTime)
    {
        IReadOnlyList<GameBehavior> behaviors = m_scene.GetComponents<GameBehavior>();
        for (int i = 0; i < behaviors.Count; i++)
        {
            if (!m_scene.canDispatch)
                break;
            GameBehavior behavior = behaviors[i];
            if (Prepare(behavior) && behavior.isActiveAndEnabled && behavior.lifecycleStartCalled && m_scene.canDispatch)
                behavior.DispatchLateUpdate(deltaTime);
        }
    }

    internal void Destroy(GameBehavior behavior)
    {
        if (behavior.lifecycleDestroyCalled)
            return;
        behavior.lifecycleDestroyCalled = true;
        if (behavior.lifecycleWasEnabled)
        {
            behavior.lifecycleWasEnabled = false;
            behavior.DispatchDisable();
        }
        behavior.DispatchDestroy();
    }

    private bool Prepare(GameBehavior behavior)
    {
        if (behavior.isDestroyed || behavior.lifecycleDestroyCalled)
            return false;

        bool active = behavior.isActiveAndEnabled;
        if (active && !behavior.lifecycleAwakeCalled)
        {
            behavior.lifecycleAwakeCalled = true;
            behavior.DispatchAwake();
            if (!m_scene.canDispatch || behavior.isDestroyed || behavior.lifecycleDestroyCalled)
                return false;
        }

        if (active && !behavior.lifecycleWasEnabled)
        {
            behavior.lifecycleWasEnabled = true;
            behavior.DispatchEnable();
            if (!m_scene.canDispatch || behavior.isDestroyed || behavior.lifecycleDestroyCalled)
                return false;
        }
        else if (!active && behavior.lifecycleWasEnabled)
        {
            behavior.lifecycleWasEnabled = false;
            behavior.DispatchDisable();
            if (!m_scene.canDispatch || behavior.isDestroyed || behavior.lifecycleDestroyCalled)
                return false;
        }

        return true;
    }
}
