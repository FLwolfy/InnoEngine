using System;
using System.Collections.Generic;

namespace Inno.Engine.Scene;

/// <summary>Orders systems and coordinates scene execution phases.</summary>
internal sealed class SceneSystemScheduler
{
    private readonly GameScene m_scene;
    private readonly BehaviorLifecycleRunner m_behaviors;
    private readonly List<GameSystem> m_systems = [];

    internal SceneSystemScheduler(GameScene scene)
    {
        m_scene = scene;
        m_behaviors = new BehaviorLifecycleRunner(scene);
    }

    internal TSystem Add<TSystem>() where TSystem : GameSystem, new()
    {
        var system = new TSystem();
        Add(system);
        return system;
    }

    internal void Add(GameSystem system)
    {
        ArgumentNullException.ThrowIfNull(system);
        if (m_systems.Contains(system))
            throw new InvalidOperationException($"System '{system.GetType().FullName}' is already registered with scene '{m_scene.name}'.");
        system.Attach(m_scene);
        m_systems.Add(system);
        m_systems.Sort(static (left, right) => left.order.CompareTo(right.order));
    }

    internal bool Remove(GameSystem system)
    {
        if (!m_systems.Remove(system))
            return false;
        system.Detach();
        return true;
    }

    internal void DestroyBehavior(GameBehavior behavior) => m_behaviors.Destroy(behavior);

    internal void FixedUpdate(float fixedDeltaTime)
    {
        using IDisposable iteration = m_scene.BeginExecutionPhase();
        m_behaviors.FixedUpdate(fixedDeltaTime);
        GameSystem[] systems = [.. m_systems];
        for (int i = 0; i < systems.Length; i++)
        {
            if (!m_scene.canDispatch)
                break;
            systems[i].DispatchFixedUpdate(fixedDeltaTime);
        }
    }

    internal void Update(float deltaTime)
    {
        using IDisposable iteration = m_scene.BeginExecutionPhase();
        m_behaviors.Update(deltaTime);
        GameSystem[] systems = [.. m_systems];
        for (int i = 0; i < systems.Length; i++)
        {
            if (!m_scene.canDispatch)
                break;
            systems[i].DispatchUpdate(deltaTime);
        }
    }

    internal void LateUpdate(float deltaTime)
    {
        using IDisposable iteration = m_scene.BeginExecutionPhase();
        m_behaviors.LateUpdate(deltaTime);
        GameSystem[] systems = [.. m_systems];
        for (int i = 0; i < systems.Length; i++)
        {
            if (!m_scene.canDispatch)
                break;
            systems[i].DispatchLateUpdate(deltaTime);
        }
    }

    internal void Clear()
    {
        GameSystem[] systems = [.. m_systems];
        m_systems.Clear();
        for (int i = 0; i < systems.Length; i++)
            systems[i].Detach();
    }
}
