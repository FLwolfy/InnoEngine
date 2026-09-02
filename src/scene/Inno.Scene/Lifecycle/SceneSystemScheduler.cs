using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Reflection;

using Inno.Assets;
using Inno.Extensibility.Types;
using Inno.Core.Storage;

namespace Inno.Scene;

/// <summary>
/// Indexes systems and coordinates scene execution phases.
/// </summary>
internal sealed class SceneSystemScheduler
{
    private readonly GameScene m_scene;
    private readonly SceneTypeCatalog m_types;
    private readonly GameBehaviorLifecycleRunner m_behaviors;
    private readonly IndexedObjectStore<SystemEntry> m_systems = new();
    private readonly IndexedObjectKey<GameSystem> m_systemKey;
    private readonly IndexedObjectKey<Guid> m_persistentIdKey;
    private readonly IndexedObjectKey<int> m_typeKey;
    private readonly List<SystemEntry> m_displayOrder = [];
    private ReadOnlyCollection<GameSystem>? m_displayView;
    private SystemEntry[]? m_executionSnapshot;

    internal SceneSystemScheduler(GameScene scene, SceneTypeCatalog types)
    {
        m_scene = scene;
        m_types = types;
        m_behaviors = new GameBehaviorLifecycleRunner(scene);
        m_systemKey = m_systems.DefineKey<GameSystem>("scene.system", IndexedObjectKeyFlags.Unique);
        m_persistentIdKey = m_systems.DefineKey<Guid>(
            "scene.system.persistent-id",
            IndexedObjectKeyFlags.Unique);
        m_typeKey = m_systems.DefineKey<int>("scene.system.type");
    }

    internal TSystem Add<TSystem>() where TSystem : GameSystem, new()
        => (TSystem)Add(typeof(TSystem), persistentId: null, invokeReset: true);

    internal GameSystem Add(Type systemType, Guid? persistentId, bool invokeReset)
    {
        ArgumentNullException.ThrowIfNull(systemType);
        if (!m_types.TryGetSystem(systemType, out SceneSystemTypeDescriptor? descriptor) ||
            !descriptor!.isConcrete)
        {
            throw new ArgumentException(
                $"Type '{systemType.FullName}' is not an active concrete GameSystem.",
                nameof(systemType));
        }

        ConstructorInfo? constructor = systemType.GetConstructor(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            Type.EmptyTypes,
            modifiers: null);
        if (constructor is null)
            throw new InvalidOperationException($"GameSystem '{descriptor.displayName}' requires a parameterless constructor.");
        var system = (GameSystem)(constructor.Invoke(null)
            ?? throw new InvalidOperationException($"Could not create GameSystem '{descriptor.displayName}'."));
        Add(system, descriptor, persistentId, invokeReset);
        return system;
    }

    internal MissingGameSystem AddMissing(
        TypeRef missingType,
        string missingTypeName,
        ReadOnlySpan<byte> serializedState,
        Guid? persistentId,
        IReadOnlyList<AssetDependency>? dependencies = null)
    {
        var system = new MissingGameSystem(
            missingType,
            missingTypeName,
            serializedState,
            dependencies);
        Add(
            system,
            m_types.GetSystem(typeof(MissingGameSystem)),
            persistentId,
            invokeReset: false);
        return system;
    }

    internal void Add(GameSystem system, Guid? persistentId = null, bool invokeReset = true)
    {
        ArgumentNullException.ThrowIfNull(system);
        SceneSystemTypeDescriptor descriptor = m_types.GetSystem(system.GetType());
        Add(system, descriptor, persistentId, invokeReset);
    }

    internal bool Remove(GameSystem system)
    {
        if (!TryGetEntry(system, out SystemEntry? entry))
            return false;
        m_systems.Remove(entry!);
        m_displayOrder.Remove(entry!);
        RefreshDisplayIndices();
        InvalidateSnapshots();
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
        if (!TryGetEntry(system, out _))
            throw new InvalidOperationException($"GameSystem '{system.GetType().FullName}' is not registered.");
        system.DispatchReset();
    }

    internal IReadOnlyList<GameSystem> GetSystems()
    {
        if (m_displayView is not null)
            return m_displayView;

        var snapshot = new GameSystem[m_displayOrder.Count];
        for (int i = 0; i < snapshot.Length; i++)
            snapshot[i] = m_displayOrder[i].system;
        return m_displayView = Array.AsReadOnly(snapshot);
    }

    internal int GetIndex(GameSystem system)
    {
        if (!TryGetEntry(system, out SystemEntry? entry))
            throw new InvalidOperationException("The GameSystem is not registered with this scene.");
        return entry!.displayIndex;
    }

    internal void SetIndex(GameSystem system, int systemIndex)
    {
        if (!TryGetEntry(system, out SystemEntry? entry))
            throw new InvalidOperationException("The GameSystem is not registered with this scene.");
        int currentIndex = entry!.displayIndex;
        int targetIndex = Math.Clamp(systemIndex, 0, m_displayOrder.Count - 1);
        if (currentIndex == targetIndex)
            return;
        m_displayOrder.RemoveAt(currentIndex);
        m_displayOrder.Insert(targetIndex, entry);
        RefreshDisplayIndices();
        InvalidateSnapshots();
    }

    internal void ReplaceForReload(
        GameSystem previous,
        GameSystem replacement,
        int replacementRuntimeTypeId)
    {
        if (!TryGetEntry(previous, out SystemEntry? entry))
            throw new InvalidOperationException("The GameSystem being replaced is not registered.");
        if (TryGetEntry(replacement, out _))
            throw new InvalidOperationException("The replacement GameSystem is already registered.");

        bool attachedHere = replacement.ownerScene is null;
        if (attachedHere)
            replacement.Attach(m_scene);
        else if (!ReferenceEquals(replacement.ownerScene, m_scene))
            throw new InvalidOperationException("The replacement GameSystem belongs to another scene.");
        Guid persistentId = previous.identity.persistentId;
        try
        {
            _ = previous.ReleaseIdentityForReplacement();
            replacement.RegisterIdentity(persistentId);
            m_systems.Add(entry!)
                .Set(m_systemKey, replacement)
                .Set(m_typeKey, replacementRuntimeTypeId);
            entry!.system = replacement;
            entry.runtimeTypeId = replacementRuntimeTypeId;
            InvalidateSnapshots();
        }
        catch (Exception exception)
        {
            List<Exception>? rollbackFailures = null;
            if (replacement.identity.runtimeId is not null)
            {
                try
                {
                    _ = replacement.ReleaseIdentityForReplacement();
                }
                catch (Exception rollbackFailure)
                {
                    rollbackFailures ??= [];
                    rollbackFailures.Add(rollbackFailure);
                }
            }
            if (previous.identity.runtimeId is null)
            {
                try
                {
                    previous.RegisterIdentity(persistentId);
                }
                catch (Exception rollbackFailure)
                {
                    rollbackFailures ??= [];
                    rollbackFailures.Add(rollbackFailure);
                }
            }
            if (attachedHere && !replacement.isDestroyed)
            {
                try
                {
                    replacement.Detach();
                }
                catch (Exception rollbackFailure)
                {
                    rollbackFailures ??= [];
                    rollbackFailures.Add(rollbackFailure);
                }
            }
            if (rollbackFailures is null)
                throw;
            throw new InvalidOperationException(
                "GameSystem hot-reload replacement failed and its identity rollback was incomplete.",
                new AggregateException([exception, .. rollbackFailures]));
        }
    }

    internal void NotifyGameBehaviorActivationChanged(GameBehavior behavior)
        => m_behaviors.Refresh(behavior);

    internal void NotifyGameSystemActivationChanged(GameSystem system)
    {
        if (TryGetEntry(system, out _))
            _ = SceneLifecycle.Prepare(system, m_scene);
    }

    internal void NotifyHierarchyActivationChanged()
        => m_behaviors.RefreshAll();

    internal void DestroyGameBehavior(GameBehavior behavior) => m_behaviors.Destroy(behavior);

    internal void FixedUpdate()
    {
        using IDisposable iteration = m_scene.BeginExecutionPhase();
        m_behaviors.FixedUpdate();
        SystemEntry[] systems = GetExecutionSnapshot();
        for (int i = 0; i < systems.Length; i++)
        {
            GameSystem system = systems[i].system;
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
        SystemEntry[] systems = GetExecutionSnapshot();
        for (int i = 0; i < systems.Length; i++)
        {
            GameSystem system = systems[i].system;
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
        SystemEntry[] systems = GetExecutionSnapshot();
        for (int i = 0; i < systems.Length; i++)
        {
            GameSystem system = systems[i].system;
            if (!m_scene.canDispatch)
                break;
            if (SceneLifecycle.Prepare(system, m_scene) &&
                system.isActiveAndEnabled &&
                system.lifecycleStartCalled)
            {
                system.DispatchLateUpdate();
            }
        }
    }

    internal void Clear()
    {
        GameSystem[] systems = [.. GetSystems()];
        m_displayOrder.Clear();
        m_systems.RemoveAll();
        InvalidateSnapshots();
        for (int i = 0; i < systems.Length; i++)
        {
            GameSystem system = systems[i];
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

    private void Add(
        GameSystem system,
        SceneSystemTypeDescriptor descriptor,
        Guid? persistentId,
        bool invokeReset)
    {
        if (TryGetEntry(system, out _))
        {
            throw new InvalidOperationException(
                $"System '{descriptor.displayName}' is already registered with scene '{m_scene.name}'.");
        }
        if (!descriptor.allowsMultiple && m_systems.First(m_typeKey, descriptor.runtimeTypeId) is not null)
        {
            throw new InvalidOperationException(
                $"Scene '{m_scene.name}' already contains GameSystem '{descriptor.displayName}'.");
        }

        system.Attach(m_scene);
        SystemEntry? entry = null;
        try
        {
            system.RegisterIdentity(persistentId);
            entry = new SystemEntry(system, descriptor.runtimeTypeId, m_displayOrder.Count);
            m_systems.Add(entry)
                .Set(m_systemKey, system)
                .Set(m_persistentIdKey, system.identity.persistentId)
                .Set(m_typeKey, descriptor.runtimeTypeId);
            m_displayOrder.Add(entry);
            InvalidateSnapshots();
            if (invokeReset)
                system.DispatchReset();
        }
        catch
        {
            if (entry is not null)
            {
                m_displayOrder.Remove(entry);
                m_systems.Remove(entry);
                RefreshDisplayIndices();
                InvalidateSnapshots();
            }
            if (!system.isDestroyed)
                system.Detach();
            throw;
        }
    }

    private bool TryGetEntry(GameSystem system, out SystemEntry? entry)
    {
        entry = m_systems.First(m_systemKey, system);
        return entry is not null;
    }

    private SystemEntry[] GetExecutionSnapshot()
    {
        if (m_executionSnapshot is null)
            m_executionSnapshot = [.. m_displayOrder];

        bool needsSort = false;
        for (int i = 0; i < m_executionSnapshot.Length; i++)
        {
            SystemEntry entry = m_executionSnapshot[i];
            int executionOrder = entry.system.order;
            if (entry.executionOrder == executionOrder)
                continue;
            entry.executionOrder = executionOrder;
            needsSort = true;
        }
        if (needsSort || !IsExecutionOrderValid(m_executionSnapshot))
            Array.Sort(m_executionSnapshot, SystemEntryExecutionComparer.instance);
        return m_executionSnapshot;
    }

    private static bool IsExecutionOrderValid(SystemEntry[] entries)
    {
        for (int i = 1; i < entries.Length; i++)
        {
            if (SystemEntryExecutionComparer.instance.Compare(entries[i - 1], entries[i]) > 0)
                return false;
        }
        return true;
    }

    private void RefreshDisplayIndices()
    {
        for (int i = 0; i < m_displayOrder.Count; i++)
            m_displayOrder[i].displayIndex = i;
    }

    private void InvalidateSnapshots()
    {
        m_displayView = null;
        m_executionSnapshot = null;
    }

    private sealed class SystemEntry(
        GameSystem system,
        int runtimeTypeId,
        int displayIndex)
    {
        internal GameSystem system { get; set; } = system;
        internal int runtimeTypeId { get; set; } = runtimeTypeId;
        internal int displayIndex { get; set; } = displayIndex;
        internal int executionOrder { get; set; } = system.order;
    }

    private sealed class SystemEntryExecutionComparer : IComparer<SystemEntry>
    {
        internal static SystemEntryExecutionComparer instance { get; } = new();

        /// <summary>
        /// Compares two values according to the deterministic ordering used by this collection.
        /// </summary>
        /// <param name="left">
        /// The left consumed by compare; ownership remains with the caller unless explicitly stated otherwise.
        /// </param>
        /// <param name="right">
        /// The right consumed by compare; ownership remains with the caller unless explicitly stated otherwise.
        /// </param>
        /// <returns>
        /// The scalar result calculated from the supplied inputs.
        /// </returns>
        public int Compare(SystemEntry? left, SystemEntry? right)
        {
            if (ReferenceEquals(left, right))
                return 0;
            if (left is null)
                return -1;
            if (right is null)
                return 1;
            int orderComparison = left.executionOrder.CompareTo(right.executionOrder);
            return orderComparison != 0
                ? orderComparison
                : left.displayIndex.CompareTo(right.displayIndex);
        }
    }
}
