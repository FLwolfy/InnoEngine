using System.Collections.Generic;

using Inno.Core.ECS;
using Inno.Engine.Scene.Components;
using EcsSystem = Inno.Core.ECS.System;

namespace Inno.Engine.Scene.Systems;

/// <summary>
/// Dispatches user-facing GameBehavior lifecycle callbacks.
/// </summary>
public sealed class BehaviorLifecycleSystem : EcsSystem
{
    private readonly HashSet<GameBehavior> m_tracked = [];
    private readonly HashSet<GameBehavior> m_seenThisStage = [];

    public override int order => 0;

    public override void FixedProcess(World world, float fixedDeltaTime)
    {
        foreach (GameBehavior component in Enumerate(world))
        {
            if (!PrepareForUpdate(component))
            {
                continue;
            }

            component.FixedUpdate(fixedDeltaTime);
        }

        SweepDestroyed();
    }

    public override void Process(World world, float deltaTime)
    {
        foreach (GameBehavior component in Enumerate(world))
        {
            if (!PrepareForUpdate(component))
            {
                continue;
            }

            component.Update(deltaTime);
        }

        SweepDestroyed();
    }

    public override void LateProcess(World world, float deltaTime)
    {
        foreach (GameBehavior component in Enumerate(world))
        {
            if (!PrepareForUpdate(component))
            {
                continue;
            }

            component.LateUpdate(deltaTime);
        }

        SweepDestroyed();
    }

    private IEnumerable<GameBehavior> Enumerate(World world)
    {
        m_seenThisStage.Clear();

        foreach (Entity entity in world.ViewEntitiesFast())
        {
            int? activeEntityId = entity.identity.runtimeId;
            if (activeEntityId is null)
            {
                continue;
            }

            foreach (GameBehavior component in world.ViewComponents<GameBehavior>(activeEntityId.Value))
            {
                m_seenThisStage.Add(component);
                m_tracked.Add(component);
                yield return component;
            }
        }
    }

    private bool PrepareForUpdate(GameBehavior component)
    {
        if (!component.lifecycleAwakeCalled)
        {
            component.Awake();
            component.lifecycleAwakeCalled = true;
        }

        if (!IsRuntimeEnabled(component))
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

    private static bool IsRuntimeEnabled(GameBehavior component)
    {
        if (!component.enabled)
        {
            return false;
        }

        return component.gameObject?.activeInHierarchy ?? false;
    }

    private void SweepDestroyed()
    {
        List<GameBehavior> destroyed = [];
        foreach (GameBehavior component in m_tracked)
        {
            if (!m_seenThisStage.Contains(component))
            {
                destroyed.Add(component);
            }
        }

        for (int i = 0; i < destroyed.Count; i++)
        {
            GameBehavior component = destroyed[i];
            if (component.lifecycleWasEnabled)
            {
                component.OnDisable();
            }

            component.OnDestroy();
            m_tracked.Remove(component);
        }
    }
}
