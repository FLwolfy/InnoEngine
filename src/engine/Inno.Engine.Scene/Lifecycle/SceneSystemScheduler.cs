using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

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
        => (TSystem)Add(typeof(TSystem), persistentId: null, invokeReset: true);

    internal GameSystem Add(Type systemType, Guid? persistentId, bool invokeReset)
    {
        ArgumentNullException.ThrowIfNull(systemType);
        if (!typeof(GameSystem).IsAssignableFrom(systemType) || systemType.IsAbstract || !systemType.IsClass)
            throw new ArgumentException($"Type '{systemType.FullName}' is not a concrete GameSystem.", nameof(systemType));
        ConstructorInfo? constructor = systemType.GetConstructor(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            Type.EmptyTypes,
            modifiers: null);
        if (constructor is null)
            throw new InvalidOperationException($"GameSystem '{systemType.FullName}' requires a parameterless constructor.");
        var system = (GameSystem)(constructor.Invoke(null)
            ?? throw new InvalidOperationException($"Could not create GameSystem '{systemType.FullName}'."));
        Add(system, persistentId, invokeReset);
        return system;
    }

    internal void Add(GameSystem system, Guid? persistentId = null, bool invokeReset = true)
    {
        ArgumentNullException.ThrowIfNull(system);
        if (m_systems.Contains(system))
            throw new InvalidOperationException(
                $"System '{system.GetType().FullName}' is already registered with scene '{m_scene.name}'.");
        Type systemType = system.GetType();
        bool allowsMultiple = systemType.IsDefined(typeof(AllowMultipleSystemAttribute), inherit: false);
        if (!allowsMultiple && m_systems.Any(existing => existing.GetType() == systemType))
            throw new InvalidOperationException($"Scene '{m_scene.name}' already contains GameSystem '{systemType.FullName}'.");

        system.Attach(m_scene);
        try
        {
            system.RegisterIdentity(persistentId);
            m_systems.Add(system);
            Sort();
            if (invokeReset)
                system.DispatchReset();
        }
        catch
        {
            m_systems.Remove(system);
            if (!system.isDestroyed)
                system.Detach();
            throw;
        }
    }

    internal bool Remove(GameSystem system)
    {
        if (!m_systems.Remove(system))
            return false;
        try
        {
            SceneLifecycle.Destroy(system);
        }
        finally
        {
            if (!system.isDestroyed)
                system.Detach();
        }
        return true;
    }

    internal void Reset(GameSystem system)
    {
        if (!m_systems.Contains(system))
            throw new InvalidOperationException($"GameSystem '{system.GetType().FullName}' is not registered.");
        system.DispatchReset();
        Sort();
    }

    internal IReadOnlyList<GameSystem> GetSystems() => m_systems.ToArray();

    internal void ReplaceForReload(GameSystem previous, GameSystem replacement)
    {
        int index = m_systems.IndexOf(previous);
        if (index < 0)
            throw new InvalidOperationException("The GameSystem being replaced is not registered.");
        bool attachedHere = replacement.ownerScene is null;
        if (attachedHere)
            replacement.Attach(m_scene);
        else if (!ReferenceEquals(replacement.ownerScene, m_scene))
            throw new InvalidOperationException("The replacement GameSystem belongs to another scene.");
        Guid persistentId = previous.ReleaseIdentityForReplacement();
        try
        {
            replacement.RegisterIdentity(persistentId);
            m_systems[index] = replacement;
            Sort();
        }
        catch
        {
            if (replacement.identity.runtimeId is not null)
                _ = replacement.ReleaseIdentityForReplacement();
            previous.RegisterIdentity(persistentId);
            if (attachedHere && !replacement.isDestroyed)
                replacement.Detach();
            throw;
        }
    }

    internal void DestroyBehavior(GameBehavior behavior) => m_behaviors.Destroy(behavior);

    internal void FixedUpdate()
    {
        using IDisposable iteration = m_scene.BeginExecutionPhase();
        m_behaviors.FixedUpdate();
        foreach (GameSystem system in m_systems.ToArray())
        {
            if (!m_scene.canDispatch)
                break;
            if (SceneLifecycle.Prepare(system, m_scene) && system.isActiveAndEnabled)
                system.DispatchFixedUpdate();
        }
    }

    internal void Update()
    {
        using IDisposable iteration = m_scene.BeginExecutionPhase();
        m_behaviors.Update();
        foreach (GameSystem system in m_systems.ToArray())
        {
            if (!m_scene.canDispatch)
                break;
            if (!SceneLifecycle.Prepare(system, m_scene) || !system.isActiveAndEnabled)
                continue;
            if (!system.lifecycleStartCalled)
            {
                system.lifecycleStartCalled = true;
                ((ISceneLifecycleObject)system).DispatchStart();
                if (!m_scene.canDispatch || system.isDestroyed)
                    break;
            }
            system.DispatchUpdate();
        }
    }

    internal void LateUpdate()
    {
        using IDisposable iteration = m_scene.BeginExecutionPhase();
        m_behaviors.LateUpdate();
        foreach (GameSystem system in m_systems.ToArray())
        {
            if (!m_scene.canDispatch)
                break;
            if (SceneLifecycle.Prepare(system, m_scene) &&
                system.isActiveAndEnabled &&
                system.lifecycleStartCalled)
                system.DispatchLateUpdate();
        }
    }

    internal void Clear()
    {
        GameSystem[] systems = [.. m_systems];
        m_systems.Clear();
        foreach (GameSystem system in systems)
        {
            try
            {
                SceneLifecycle.Destroy(system);
            }
            finally
            {
                if (!system.isDestroyed)
                    system.Detach();
            }
        }
    }

    private void Sort()
    {
        GameSystem[] ordered = m_systems
            .OrderBy(static system => system.order)
            .ToArray();
        m_systems.Clear();
        m_systems.AddRange(ordered);
    }
}
