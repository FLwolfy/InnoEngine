using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;

using Inno.Core.Reflection;
using Inno.Core.Storage;
using Inno.Engine.Scene.Layers;

namespace Inno.Engine.Scene;

/// <summary>Describes the structural result of removing a component.</summary>
internal enum SceneStoreRemovalKind
{
    None,
    CanceledPendingAddition,
    RemovedCommitted,
}

internal readonly record struct SceneStoreRemovedComponent(GameComponent component, bool wasCommitted);

internal sealed class SceneStore
{
    private static readonly Lock S_TYPE_CACHE_SYNC = new();
    private static readonly List<WeakReference<SceneStore>> S_STORES = [];

    private readonly IndexedObjectStore<SceneObjectRecord> m_objects = new();
    private readonly IndexedObjectKey<GameObject> m_objectKey;
    private readonly IndexedObjectKey<Guid> m_objectPersistentIdKey;
    private readonly IndexedObjectKey<string> m_objectNameKey;
    private readonly IndexedObjectKey<string> m_objectTagKey;
    private readonly IndexedObjectKey<GameLayer> m_objectLayerKey;
    private readonly IndexedObjectKey<bool> m_objectCommittedKey;
    private readonly IndexedObjectKey<long> m_objectOrderKey;

    private readonly IndexedObjectStore<ComponentEntry> m_components = new();
    private readonly IndexedObjectKey<GameComponent> m_componentKey;
    private readonly IndexedObjectKey<Guid> m_componentPersistentIdKey;
    private readonly IndexedObjectKey<SceneObjectRecord> m_componentOwnerKey;
    private readonly IndexedObjectKey<int> m_componentTypeKey;
    private readonly IndexedObjectKey<bool> m_componentCommittedKey;

    private readonly List<PendingObjectAddition> m_pendingObjectAdditions = [];
    private readonly List<PendingObjectRemoval> m_pendingObjectRemovals = [];
    private readonly List<PendingComponentAddition> m_pendingComponentAdditions = [];
    private readonly List<PendingComponentRemoval> m_pendingComponentRemovals = [];
    private readonly Dictionary<int, GameComponent[]> m_componentQueryCache = [];
    private readonly Dictionary<int, object> m_typedComponentQueryCache = [];
    private readonly Dictionary<ComponentQueryKey, ReadOnlyCollection<GameObject>> m_objectQueryCache = [];
    private GameObject[]? m_objectSnapshotCache;
    private long m_nextObjectOrder;
    private int m_executionDepth;
    private bool m_clearRequested;

    internal SceneStore()
    {
        lock (S_TYPE_CACHE_SYNC)
        {
            S_STORES.RemoveAll(static reference => !reference.TryGetTarget(out _));
            S_STORES.Add(new WeakReference<SceneStore>(this));
        }

        m_objectKey = m_objects.DefineKey<GameObject>("scene.object", IndexedObjectKeyFlags.Unique);
        m_objectPersistentIdKey = m_objects.DefineKey<Guid>("scene.object.persistent-id", IndexedObjectKeyFlags.Unique);
        m_objectNameKey = m_objects.DefineKey<string>("scene.object.name");
        m_objectTagKey = m_objects.DefineKey<string>("scene.object.tag");
        m_objectLayerKey = m_objects.DefineKey<GameLayer>("scene.object.layer");
        m_objectCommittedKey = m_objects.DefineKey<bool>("scene.object.committed");
        m_objectOrderKey = m_objects.DefineKey<long>(
            "scene.object.order",
            IndexedObjectKeyFlags.Ordered | IndexedObjectKeyFlags.Unique,
            Comparer<long>.Default);

        m_componentKey = m_components.DefineKey<GameComponent>("scene.component", IndexedObjectKeyFlags.Unique);
        m_componentPersistentIdKey = m_components.DefineKey<Guid>(
            "scene.component.persistent-id",
            IndexedObjectKeyFlags.Unique);
        m_componentOwnerKey = m_components.DefineKey<SceneObjectRecord>("scene.component.owner");
        m_componentTypeKey = m_components.DefineKey<int>("scene.component.type");
        m_componentCommittedKey = m_components.DefineKey<bool>("scene.component.committed");
    }

    internal bool isExecuting => m_executionDepth != 0;
    internal bool hasPendingChanges =>
        m_pendingObjectAdditions.Count != 0 ||
        m_pendingObjectRemovals.Count != 0 ||
        m_pendingComponentAdditions.Count != 0 ||
        m_pendingComponentRemovals.Count != 0 ||
        m_clearRequested;

    internal void AddObject(GameObject gameObject)
    {
        ArgumentNullException.ThrowIfNull(gameObject);
        if (TryGetRecord(gameObject, out _))
        {
            throw new InvalidOperationException(
                $"GameObject '{gameObject.identity.persistentId}' is already owned by this scene store.");
        }

        var record = new SceneObjectRecord(gameObject);
        long order = m_nextObjectOrder;
        m_nextObjectOrder = checked(m_nextObjectOrder + 1);
        try
        {
            m_objects.Add(record)
                .Set(m_objectKey, gameObject)
                .Set(m_objectPersistentIdKey, gameObject.identity.persistentId)
                .Set(m_objectNameKey, gameObject.storedName)
                .Set(m_objectTagKey, gameObject.storedTag)
                .Set(m_objectLayerKey, gameObject.storedLayer)
                .Set(m_objectCommittedKey, false)
                .Set(m_objectOrderKey, order);
        }
        catch
        {
            m_objects.Remove(record);
            throw;
        }

        if (isExecuting)
        {
            m_pendingObjectAdditions.Add(new PendingObjectAddition(record));
            return;
        }
        CommitObjectAddition(record);
    }

    internal void AddComponent(
        GameObject owner,
        GameComponent component,
        SceneComponentTypeDescriptor descriptor,
        bool allowsMultiple)
    {
        ArgumentNullException.ThrowIfNull(component);
        ArgumentNullException.ThrowIfNull(descriptor);
        SceneObjectRecord record = GetAliveRecord(owner);
        if (TryGetEntry(component, out _))
        {
            throw new InvalidOperationException(
                $"GameComponent '{descriptor.displayName}' is already owned by this scene store.");
        }

        if (!allowsMultiple)
        {
            ComponentEntry? duplicate = m_components.Query()
                .Find(m_componentOwnerKey, record)
                .Find(m_componentTypeKey, descriptor.runtimeTypeId)
                .First();
            if (duplicate is { isAlive: true })
            {
                throw new InvalidOperationException(
                    $"GameObject '{owner.identity.persistentId}' already owns unique component " +
                    $"'{descriptor.displayName}'.");
            }
        }

        var entry = new ComponentEntry(record, component, descriptor.runtimeTypeId);
        try
        {
            m_components.Add(entry)
                .Set(m_componentKey, component)
                .Set(m_componentPersistentIdKey, component.identity.persistentId)
                .Set(m_componentOwnerKey, record)
                .Set(m_componentTypeKey, descriptor.runtimeTypeId)
                .Set(m_componentCommittedKey, false);
            record.components.Add(component);
        }
        catch
        {
            m_components.Remove(entry);
            throw;
        }

        if (isExecuting || !record.isCommitted)
        {
            m_pendingComponentAdditions.Add(new PendingComponentAddition(entry));
            return;
        }
        CommitComponentAddition(entry);
    }

    internal SceneStoreRemovalKind RemoveComponent(GameObject owner, GameComponent component)
    {
        if (!TryGetRecord(owner, out SceneObjectRecord? record) ||
            !record!.isAlive ||
            !TryGetEntry(component, out ComponentEntry? entry) ||
            !entry!.isAlive ||
            !ReferenceEquals(entry.owner, record))
        {
            return SceneStoreRemovalKind.None;
        }

        entry.isAlive = false;
        record.components.Remove(component);
        InvalidateStructureCaches();
        if (!entry.isCommitted)
        {
            m_components.Remove(entry);
            return SceneStoreRemovalKind.CanceledPendingAddition;
        }

        if (isExecuting)
            m_pendingComponentRemovals.Add(new PendingComponentRemoval(entry));
        else
            CommitComponentRemoval(entry);
        return SceneStoreRemovalKind.RemovedCommitted;
    }

    internal void ReplaceComponent(
        GameComponent previous,
        GameComponent replacement,
        int replacementRuntimeTypeId)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(replacement);
        if (isExecuting || hasPendingChanges)
            throw new InvalidOperationException("Components cannot be replaced during a scene execution phase.");
        if (!TryGetEntry(previous, out ComponentEntry? entry) || !entry!.isAlive)
            throw new InvalidOperationException("The component being replaced is not attached to this scene.");
        if (TryGetEntry(replacement, out _))
            throw new InvalidOperationException("The replacement component is already attached to this scene.");

        int index = entry.owner.components.IndexOf(previous);
        if (index < 0)
            throw new InvalidOperationException("The component attachment order is inconsistent.");

        m_components.Add(entry)
            .Set(m_componentKey, replacement)
            .Set(m_componentTypeKey, replacementRuntimeTypeId);
        entry.component = replacement;
        entry.runtimeTypeId = replacementRuntimeTypeId;
        entry.owner.components[index] = replacement;
        InvalidateStructureCaches();
    }

    internal IReadOnlyList<SceneStoreRemovedComponent> RemoveObject(GameObject gameObject)
    {
        if (!TryGetRecord(gameObject, out SceneObjectRecord? record) || !record!.isAlive)
            return Array.Empty<SceneStoreRemovedComponent>();

        record.isAlive = false;
        GameComponent[] attached = [.. record.components];
        var removed = new SceneStoreRemovedComponent[attached.Length];
        for (int i = 0; i < attached.Length; i++)
        {
            bool wasCommitted = TryGetEntry(attached[i], out ComponentEntry? entry) && entry!.isCommitted;
            removed[i] = new SceneStoreRemovedComponent(attached[i], wasCommitted);
            RemoveComponentEntry(record, attached[i]);
        }
        record.components.Clear();
        InvalidateStructureCaches();

        if (!record.isCommitted)
        {
            m_objects.Remove(record);
            return removed;
        }

        if (isExecuting)
            m_pendingObjectRemovals.Add(new PendingObjectRemoval(record));
        else
            CommitObjectRemoval(record);
        return removed;
    }

    internal bool Contains(GameObject gameObject)
        => TryGetRecord(gameObject, out SceneObjectRecord? record) && record!.isAlive;

    internal IReadOnlyList<GameObject> GetObjects()
    {
        if (m_objectSnapshotCache is not null)
            return m_objectSnapshotCache;

        IReadOnlyList<SceneObjectRecord> records = m_objects.Query()
            .Find(m_objectCommittedKey, true)
            .Where(static record => record.isAlive)
            .OrderBy(m_objectOrderKey)
            .Get();
        var result = new GameObject[records.Count];
        for (int i = 0; i < records.Count; i++)
            result[i] = records[i].gameObject;
        m_objectSnapshotCache = result;
        return result;
    }

    internal GameObject? FindObject(Guid persistentId)
    {
        SceneObjectRecord? record = m_objects.First(m_objectPersistentIdKey, persistentId);
        return record is { isAlive: true } ? record.gameObject : null;
    }

    internal GameObject? FindObject(string name)
        => m_objects.Query()
            .Find(m_objectNameKey, name)
            .Find(m_objectCommittedKey, true)
            .Where(static record => record.isAlive)
            .OrderBy(m_objectOrderKey)
            .First()?.gameObject;

    internal GameObject? FindObjectWithTag(string tag)
        => m_objects.Query()
            .Find(m_objectTagKey, tag)
            .Find(m_objectCommittedKey, true)
            .Where(static record => record.isAlive)
            .OrderBy(m_objectOrderKey)
            .First()?.gameObject;

    internal IReadOnlyList<GameObject> FindObjectsWithTag(string tag)
        => SelectObjects(m_objects.Query()
            .Find(m_objectTagKey, tag)
            .Find(m_objectCommittedKey, true)
            .Where(static record => record.isAlive)
            .OrderBy(m_objectOrderKey)
            .Get());

    internal GameObject? FindObjectWithLayer(GameLayer layer)
        => m_objects.Query()
            .Find(m_objectLayerKey, layer)
            .Find(m_objectCommittedKey, true)
            .Where(static record => record.isAlive)
            .OrderBy(m_objectOrderKey)
            .First()?.gameObject;

    internal IReadOnlyList<GameObject> FindObjectsWithLayer(GameLayer layer)
        => SelectObjects(m_objects.Query()
            .Find(m_objectLayerKey, layer)
            .Find(m_objectCommittedKey, true)
            .Where(static record => record.isAlive)
            .OrderBy(m_objectOrderKey)
            .Get());

    internal IReadOnlyList<GameObject> FindObjectsWithLayers(GameLayerMask layers)
    {
        if (layers == GameLayerMask.none)
            return Array.Empty<GameObject>();
        return GetObjects().Where(gameObject => layers.Contains(gameObject.layer)).ToArray();
    }

    internal void NotifyObjectMetadataChanged(GameObject gameObject)
    {
        SceneObjectRecord record = GetAliveRecord(gameObject);
        m_objects.Add(record)
            .Set(m_objectNameKey, gameObject.storedName)
            .Set(m_objectTagKey, gameObject.storedTag)
            .Set(m_objectLayerKey, gameObject.storedLayer);
    }

    internal IReadOnlyList<GameObject> GetOwnedObjects()
        => m_objects.AllFast()
            .Where(static record => record.isAlive)
            .Select(static record => record.gameObject)
            .ToArray();

    internal GameComponent? FindComponent(Guid persistentId)
    {
        ComponentEntry? entry = m_components.First(m_componentPersistentIdKey, persistentId);
        return entry is { isAlive: true } && entry.owner.isAlive ? entry.component : null;
    }

    internal IReadOnlyList<GameComponent> GetComponents(GameObject owner)
        => GetAliveRecord(owner).components.Where(IsLocallyVisible).ToArray();

    internal bool TryGetComponent<TComponent>(
        GameObject owner,
        SceneComponentTypeDescriptor requestedType,
        out TComponent? component) where TComponent : GameComponent
    {
        SceneObjectRecord record = GetAliveRecord(owner);
        for (int i = 0; i < record.components.Count; i++)
        {
            GameComponent candidate = record.components[i];
            if (TryGetEntry(candidate, out ComponentEntry? entry) &&
                entry!.isAlive &&
                requestedType.IsAssignableFrom(entry.runtimeTypeId))
            {
                component = (TComponent)candidate;
                return true;
            }
        }
        component = null;
        return false;
    }

    internal bool TryGetComponent(
        GameObject owner,
        SceneComponentTypeDescriptor requestedType,
        out GameComponent? component)
    {
        SceneObjectRecord record = GetAliveRecord(owner);
        for (int i = 0; i < record.components.Count; i++)
        {
            GameComponent candidate = record.components[i];
            if (TryGetEntry(candidate, out ComponentEntry? entry) &&
                entry!.isAlive &&
                requestedType.IsAssignableFrom(entry.runtimeTypeId))
            {
                component = candidate;
                return true;
            }
        }
        component = null;
        return false;
    }

    internal int GetComponentIndex(GameObject owner, GameComponent component)
    {
        ArgumentNullException.ThrowIfNull(component);
        SceneObjectRecord record = GetAliveRecord(owner);
        int index = record.components.IndexOf(component);
        return index >= 0 && IsLocallyVisible(component)
            ? index
            : throw new InvalidOperationException("The component is not attached to the requested GameObject.");
    }

    internal void SetComponentIndex(GameObject owner, GameComponent component, int componentIndex)
    {
        if (isExecuting || hasPendingChanges)
            throw new InvalidOperationException("Components cannot be reordered during a scene execution phase.");
        SceneObjectRecord record = GetAliveRecord(owner);
        int currentIndex = GetComponentIndex(owner, component);
        if (component is Components.Transform)
        {
            if (componentIndex != 0)
                throw new InvalidOperationException("The mandatory Transform component must remain at index zero.");
            return;
        }

        int targetIndex = Math.Clamp(componentIndex, 1, record.components.Count - 1);
        if (currentIndex == targetIndex)
            return;
        record.components.RemoveAt(currentIndex);
        record.components.Insert(targetIndex, component);
    }

    internal IReadOnlyList<TComponent> GetComponents<TComponent>(
        GameObject owner,
        SceneComponentTypeDescriptor requestedType) where TComponent : GameComponent
    {
        SceneObjectRecord record = GetAliveRecord(owner);
        var result = new List<TComponent>(record.components.Count);
        for (int i = 0; i < record.components.Count; i++)
        {
            GameComponent candidate = record.components[i];
            if (TryGetEntry(candidate, out ComponentEntry? entry) &&
                entry!.isAlive &&
                requestedType.IsAssignableFrom(entry.runtimeTypeId))
            {
                result.Add((TComponent)candidate);
            }
        }
        return result;
    }

    internal IReadOnlyList<TComponent> GetComponents<TComponent>(
        SceneComponentTypeDescriptor requestedType) where TComponent : GameComponent
    {
        if (m_typedComponentQueryCache.TryGetValue(requestedType.runtimeTypeId, out object? cached))
            return (ReadOnlyCollection<TComponent>)cached;

        GameComponent[] untyped = GetCommittedComponents(requestedType);
        var typed = new TComponent[untyped.Length];
        for (int i = 0; i < untyped.Length; i++)
            typed[i] = (TComponent)untyped[i];
        ReadOnlyCollection<TComponent> view = Array.AsReadOnly(typed);
        m_typedComponentQueryCache.Add(requestedType.runtimeTypeId, view);
        return view;
    }

    internal IReadOnlyList<GameObject> Query(SceneComponentTypeDescriptor requestedType)
        => Query(ComponentQueryKey.Create(requestedType.runtimeTypeId));

    internal IReadOnlyList<GameObject> Query(
        SceneComponentTypeDescriptor first,
        SceneComponentTypeDescriptor second)
        => Query(ComponentQueryKey.Create(first.runtimeTypeId, second.runtimeTypeId));

    internal IReadOnlyList<GameObject> Query(
        SceneComponentTypeDescriptor first,
        SceneComponentTypeDescriptor second,
        SceneComponentTypeDescriptor third)
        => Query(ComponentQueryKey.Create(
            first.runtimeTypeId,
            second.runtimeTypeId,
            third.runtimeTypeId));

    private IReadOnlyList<GameObject> Query(ComponentQueryKey query)
    {
        if (m_objectQueryCache.TryGetValue(query, out ReadOnlyCollection<GameObject>? cached))
            return cached;

        SceneComponentTypeDescriptor first = SceneTypeCatalog.GetComponent(query.first);
        SceneComponentTypeDescriptor? second = query.count >= 2
            ? SceneTypeCatalog.GetComponent(query.second)
            : null;
        SceneComponentTypeDescriptor? third = query.count >= 3
            ? SceneTypeCatalog.GetComponent(query.third)
            : null;
        GameComponent[] candidates = GetCommittedComponents(first);
        if (second is not null)
        {
            GameComponent[] secondCandidates = GetCommittedComponents(second);
            if (secondCandidates.Length < candidates.Length)
                candidates = secondCandidates;
        }
        if (third is not null)
        {
            GameComponent[] thirdCandidates = GetCommittedComponents(third);
            if (thirdCandidates.Length < candidates.Length)
                candidates = thirdCandidates;
        }

        var seen = new HashSet<GameObject>(ReferenceEqualityComparer.Instance);
        var result = new List<GameObject>();
        for (int i = 0; i < candidates.Length; i++)
        {
            GameComponent candidate = candidates[i];
            if (!TryGetEntry(candidate, out ComponentEntry? entry) ||
                !IsVisible(entry!) ||
                !seen.Add(entry!.owner.gameObject))
            {
                continue;
            }

            if (HasVisibleComponent(entry.owner, first) &&
                (second is null || HasVisibleComponent(entry.owner, second)) &&
                (third is null || HasVisibleComponent(entry.owner, third)))
            {
                result.Add(entry.owner.gameObject);
            }
        }

        cached = Array.AsReadOnly(result.ToArray());
        m_objectQueryCache.Add(query, cached);
        return cached;
    }

    internal IDisposable BeginExecutionPhase()
    {
        if (m_clearRequested)
            throw new InvalidOperationException("A scene execution phase cannot begin while the store is pending clear.");
        m_executionDepth++;
        return new ExecutionScope(this);
    }

    internal SceneStructureSnapshot CaptureStructure()
    {
        if (isExecuting || hasPendingChanges)
        {
            throw new InvalidOperationException(
                "A scene cannot be captured during an execution phase with uncommitted structural changes.");
        }

        SceneObjectStructureSnapshot[] objects = GetObjects()
            .Select(gameObject => new SceneObjectStructureSnapshot(gameObject, GetComponents(gameObject)))
            .ToArray();
        return new SceneStructureSnapshot(objects);
    }

    internal void InvalidateTypeCaches()
    {
        m_componentQueryCache.Clear();
        m_typedComponentQueryCache.Clear();
        m_objectQueryCache.Clear();
    }

    internal static void InvalidateAllTypeCaches()
    {
        lock (S_TYPE_CACHE_SYNC)
        {
            for (int i = S_STORES.Count - 1; i >= 0; i--)
            {
                if (S_STORES[i].TryGetTarget(out SceneStore? store))
                    store.InvalidateTypeCaches();
                else
                    S_STORES.RemoveAt(i);
            }
        }
    }

    internal void Clear()
    {
        InvalidateStructureCaches();
        if (isExecuting)
        {
            m_clearRequested = true;
            return;
        }
        ClearImmediately();
    }

    private GameComponent[] GetCommittedComponents(SceneComponentTypeDescriptor requestedType)
    {
        if (m_componentQueryCache.TryGetValue(requestedType.runtimeTypeId, out GameComponent[]? cached))
            return cached;

        var result = new List<GameComponent>();
        int[] concreteTypes = requestedType.assignableConcreteRuntimeTypeIds;
        for (int i = 0; i < concreteTypes.Length; i++)
        {
            foreach (ComponentEntry entry in m_components.FindFast(
                         m_componentTypeKey,
                         concreteTypes[i]))
            {
                if (IsVisible(entry))
                    result.Add(entry.component);
            }
        }

        cached = result.ToArray();
        m_componentQueryCache.Add(requestedType.runtimeTypeId, cached);
        return cached;
    }

    private bool HasVisibleComponent(
        SceneObjectRecord owner,
        SceneComponentTypeDescriptor requestedType)
    {
        for (int i = 0; i < owner.components.Count; i++)
        {
            if (TryGetEntry(owner.components[i], out ComponentEntry? entry) &&
                IsVisible(entry!) &&
                requestedType.IsAssignableFrom(entry!.runtimeTypeId))
            {
                return true;
            }
        }
        return false;
    }

    private bool IsVisible(ComponentEntry entry)
        => entry.isAlive && entry.isCommitted && entry.owner.isAlive && entry.owner.isCommitted;

    private bool IsLocallyVisible(GameComponent component)
        => TryGetEntry(component, out ComponentEntry? entry) && entry!.isAlive && entry.owner.isAlive;

    private SceneObjectRecord GetAliveRecord(GameObject owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        if (!TryGetRecord(owner, out SceneObjectRecord? record) || !record!.isAlive)
        {
            throw new InvalidOperationException(
                $"GameObject '{owner.identity.persistentId}' does not belong to this scene store.");
        }
        return record;
    }

    private bool TryGetRecord(GameObject gameObject, out SceneObjectRecord? record)
    {
        record = m_objects.First(m_objectKey, gameObject);
        return record is not null;
    }

    private bool TryGetEntry(GameComponent component, out ComponentEntry? entry)
    {
        entry = m_components.First(m_componentKey, component);
        return entry is not null;
    }

    private void RemoveComponentEntry(SceneObjectRecord record, GameComponent component)
    {
        if (!TryGetEntry(component, out ComponentEntry? entry) || !entry!.isAlive)
            return;

        entry.isAlive = false;
        record.components.Remove(component);
        if (!entry.isCommitted)
        {
            m_components.Remove(entry);
            return;
        }

        if (isExecuting)
            m_pendingComponentRemovals.Add(new PendingComponentRemoval(entry));
        else
            CommitComponentRemoval(entry);
    }

    private void CommitPendingChanges()
    {
        for (int i = 0; i < m_pendingObjectAdditions.Count; i++)
        {
            SceneObjectRecord record = m_pendingObjectAdditions[i].record;
            if (record.isAlive && !record.isCommitted)
                CommitObjectAddition(record);
        }
        for (int i = 0; i < m_pendingComponentAdditions.Count; i++)
        {
            ComponentEntry entry = m_pendingComponentAdditions[i].entry;
            if (entry.isAlive && entry.owner.isAlive && !entry.isCommitted)
                CommitComponentAddition(entry);
        }
        for (int i = 0; i < m_pendingComponentRemovals.Count; i++)
        {
            ComponentEntry entry = m_pendingComponentRemovals[i].entry;
            if (entry.isCommitted)
                CommitComponentRemoval(entry);
        }
        for (int i = 0; i < m_pendingObjectRemovals.Count; i++)
        {
            SceneObjectRecord record = m_pendingObjectRemovals[i].record;
            if (record.isCommitted)
                CommitObjectRemoval(record);
        }

        m_pendingObjectAdditions.Clear();
        m_pendingObjectRemovals.Clear();
        m_pendingComponentAdditions.Clear();
        m_pendingComponentRemovals.Clear();
    }

    private void CommitObjectAddition(SceneObjectRecord record)
    {
        record.isCommitted = true;
        m_objects.Add(record).Set(m_objectCommittedKey, true);
        InvalidateStructureCaches();
    }

    private void CommitObjectRemoval(SceneObjectRecord record)
    {
        record.isCommitted = false;
        m_objects.Remove(record);
        InvalidateStructureCaches();
    }

    private void CommitComponentAddition(ComponentEntry entry)
    {
        entry.isCommitted = true;
        m_components.Add(entry).Set(m_componentCommittedKey, true);
        InvalidateStructureCaches();
    }

    private void CommitComponentRemoval(ComponentEntry entry)
    {
        entry.isCommitted = false;
        m_components.Remove(entry);
        InvalidateStructureCaches();
    }

    private void InvalidateStructureCaches()
    {
        m_objectSnapshotCache = null;
        InvalidateTypeCaches();
    }

    private void EndExecutionPhase()
    {
        if (m_executionDepth <= 0)
            throw new InvalidOperationException("Scene execution phase scopes are unbalanced.");
        m_executionDepth--;
        if (m_executionDepth != 0)
            return;

        if (m_clearRequested)
            ClearImmediately();
        else
            CommitPendingChanges();
    }

    private void ClearImmediately()
    {
        m_pendingObjectAdditions.Clear();
        m_pendingObjectRemovals.Clear();
        m_pendingComponentAdditions.Clear();
        m_pendingComponentRemovals.Clear();
        m_components.RemoveAll();
        m_objects.RemoveAll();
        m_nextObjectOrder = 0;
        m_clearRequested = false;
        InvalidateStructureCaches();
    }

    private static IReadOnlyList<GameObject> SelectObjects(IReadOnlyList<SceneObjectRecord> records)
    {
        var result = new GameObject[records.Count];
        for (int i = 0; i < records.Count; i++)
            result[i] = records[i].gameObject;
        return result;
    }

    private sealed class ComponentEntry
    {
        internal ComponentEntry(SceneObjectRecord owner, GameComponent component, int runtimeTypeId)
        {
            this.owner = owner;
            this.component = component;
            this.runtimeTypeId = runtimeTypeId;
        }

        internal SceneObjectRecord owner { get; }
        internal GameComponent component { get; set; }
        internal int runtimeTypeId { get; set; }
        internal bool isAlive { get; set; } = true;
        internal bool isCommitted { get; set; }
    }

    private readonly record struct PendingObjectAddition(SceneObjectRecord record);
    private readonly record struct PendingObjectRemoval(SceneObjectRecord record);
    private readonly record struct PendingComponentAddition(ComponentEntry entry);
    private readonly record struct PendingComponentRemoval(ComponentEntry entry);

    private readonly record struct ComponentQueryKey(int first, int second, int third, int count)
    {
        internal static ComponentQueryKey Create(int first)
            => new(first, default, default, 1);

        internal static ComponentQueryKey Create(int first, int second)
            => first == second
                ? Create(first)
                : Compare(first, second) < 0
                    ? new ComponentQueryKey(first, second, default, 2)
                    : new ComponentQueryKey(second, first, default, 2);

        internal static ComponentQueryKey Create(int first, int second, int third)
        {
            if (Compare(first, second) > 0)
                (first, second) = (second, first);
            if (Compare(second, third) > 0)
                (second, third) = (third, second);
            if (Compare(first, second) > 0)
                (first, second) = (second, first);
            if (first == third)
                return Create(first);
            if (first == second)
                return Create(first, third);
            if (second == third)
                return Create(first, second);
            return new ComponentQueryKey(first, second, third, 3);
        }

        private static int Compare(int left, int right)
            => left.CompareTo(right);
    }

    private sealed class ExecutionScope : IDisposable
    {
        private SceneStore? m_store;

        internal ExecutionScope(SceneStore store)
        {
            m_store = store;
        }

        public void Dispose()
        {
            SceneStore? store = Interlocked.Exchange(ref m_store, null);
            store?.EndExecutionPhase();
        }
    }
}
