using System.Collections.Generic;

using Inno.Core.ECS;
using Inno.Engine.Scene.Components;
using EcsSystem = Inno.Core.ECS.System;

namespace Inno.Engine.Scene.Systems;

/// <summary>
/// Dispatches user-facing GameComponent lifecycle callbacks.
/// </summary>
public sealed class GameComponentLifecycleSystem : EcsSystem
{
    private readonly HashSet<GameComponent> m_tracked = [];
    private readonly HashSet<GameComponent> m_seenThisStage = [];
    private readonly Dictionary<GameComponent, bool> m_activeByComponent = new();

    public override int order => 0;

    public override void FixedProcess(World world, float fixedDeltaTime)
    {
        foreach (GameComponent component in Enumerate(world))
        {
            if (!PrepareForUpdate(world, component))
            {
                continue;
            }

            component.FixedUpdate(fixedDeltaTime);
        }

        SweepDestroyed();
    }

    public override void Process(World world, float deltaTime)
    {
        foreach (GameComponent component in Enumerate(world))
        {
            if (!PrepareForUpdate(world, component))
            {
                continue;
            }

            component.Update(deltaTime);
        }

        SweepDestroyed();
    }

    public override void LateProcess(World world, float deltaTime)
    {
        foreach (GameComponent component in Enumerate(world))
        {
            if (!PrepareForUpdate(world, component))
            {
                continue;
            }

            component.LateUpdate(deltaTime);
        }

        SweepDestroyed();
    }

    private IEnumerable<GameComponent> Enumerate(World world)
    {
        m_seenThisStage.Clear();
        m_activeByComponent.Clear();

        foreach (Entity entity in world.ViewEntitiesFast())
        {
            if (entity.identity.runtimeId is not int entityId)
            {
                continue;
            }

            bool activeInHierarchy = true;
            IReadOnlyList<ActiveState> activeStates = world.ViewComponents<ActiveState>(entityId);
            if (activeStates.Count != 0)
            {
                activeInHierarchy = activeStates[0].activeInHierarchy;
            }

            foreach (GameComponent component in world.ViewComponents<GameComponent>(entityId))
            {
                m_activeByComponent[component] = activeInHierarchy;
                m_seenThisStage.Add(component);
                m_tracked.Add(component);
                yield return component;
            }
        }
    }

    private bool PrepareForUpdate(World world, GameComponent component)
    {
        if (!component.lifecycleAwakeCalled)
        {
            component.Awake();
            component.lifecycleAwakeCalled = true;
        }

        if (!IsRuntimeEnabled(world, component))
        {
            if (component.lifecycleWasEnabled)
            {
                component.OnDisable();
                component.lifecycleWasEnabled = false;
            }

            return false;
        }

        if (!component.lifecycleWasEnabled)
        {
            component.OnEnable();
            component.lifecycleWasEnabled = true;
        }

        if (!component.lifecycleStartCalled)
        {
            component.Start();
            component.lifecycleStartCalled = true;
        }

        return true;
    }

    private bool IsRuntimeEnabled(World world, GameComponent component)
    {
        if (!component.enabled)
        {
            return false;
        }

        return !m_activeByComponent.TryGetValue(component, out bool activeInHierarchy) || activeInHierarchy;
    }

    private void SweepDestroyed()
    {
        List<GameComponent> destroyed = [];
        foreach (GameComponent component in m_tracked)
        {
            if (!m_seenThisStage.Contains(component))
            {
                destroyed.Add(component);
            }
        }

        for (int i = 0; i < destroyed.Count; i++)
        {
            GameComponent component = destroyed[i];
            if (component.lifecycleWasEnabled)
            {
                component.OnDisable();
            }

            component.OnDestroy();
            m_tracked.Remove(component);
        }
    }
}
