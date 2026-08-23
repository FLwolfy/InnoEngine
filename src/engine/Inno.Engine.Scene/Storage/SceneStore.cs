using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

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
    private readonly ObjectPool<SceneObjectRecord> m_committedObjects = new();
    private readonly Dictionary<GameObject, SceneObjectRecord> m_records = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<GameComponent, ComponentEntry> m_components = new(ReferenceEqualityComparer.Instance);
    private readonly ComponentBucketRegistry m_buckets = new();
    private readonly List<PendingObjectAddition> m_pendingObjectAdditions = [];
    private readonly List<PendingObjectRemoval> m_pendingObjectRemovals = [];
    private readonly List<PendingComponentAddition> m_pendingComponentAdditions = [];
    private readonly List<PendingComponentRemoval> m_pendingComponentRemovals = [];
    private readonly Dictionary<Type, GameComponent[]> m_componentQueryCache = [];
    private readonly Dictionary<string, GameObject[]> m_objectQueryCache = new(StringComparer.Ordinal);
    private Dictionary<string, GameObject>? m_firstObjectByName;
    private Dictionary<string, GameObject[]>? m_objectsByTag;
    private Dictionary<GameLayer, GameObject[]>? m_objectsByLayer;
    private GameObject[]? m_objectSnapshotCache;
    private int m_executionDepth;
    private bool m_clearRequested;

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
        if (m_records.ContainsKey(gameObject))
            throw new InvalidOperationException($"GameObject '{gameObject.identity.persistentId}' is already owned by this scene store.");

        var record = new SceneObjectRecord(gameObject);
        m_records.Add(gameObject, record);
        if (isExecuting)
        {
            m_pendingObjectAdditions.Add(new PendingObjectAddition(record));
            return;
        }

        CommitObjectAddition(record);
    }

    internal void AddComponent(GameObject owner, GameComponent component, bool allowsMultiple)
    {
        ArgumentNullException.ThrowIfNull(component);
        SceneObjectRecord record = GetAliveRecord(owner);
        Type concreteType = component.GetType();
        if (m_components.ContainsKey(component))
            throw new InvalidOperationException($"GameComponent '{concreteType.FullName}' is already owned by this scene store.");
        if (!allowsMultiple && record.components.Any(existing => existing.GetType() == concreteType))
        {
            throw new InvalidOperationException(
                $"GameObject '{owner.identity.persistentId}' already owns unique component '{concreteType.FullName}'.");
        }

        IComponentBucket bucket = m_buckets.GetOrCreate(concreteType);
        var entry = new ComponentEntry(record, component, bucket);
        record.components.Add(component);
        m_components.Add(component, entry);
        if (isExecuting || !record.isCommitted)
        {
            m_pendingComponentAdditions.Add(new PendingComponentAddition(entry));
            return;
        }

        CommitComponentAddition(entry);
    }

    internal SceneStoreRemovalKind RemoveComponent(GameObject owner, GameComponent component)
    {
        if (!m_records.TryGetValue(owner, out SceneObjectRecord? record) ||
            !record.isAlive ||
            !m_components.TryGetValue(component, out ComponentEntry? entry) ||
            !entry.isAlive ||
            !ReferenceEquals(entry.owner, record))
        {
            return SceneStoreRemovalKind.None;
        }

        entry.isAlive = false;
        record.components.Remove(component);
        InvalidateQueryCaches();
        if (!entry.isCommitted)
        {
            m_components.Remove(component);
            m_buckets.RemoveIfEmpty(entry.bucket);
            return SceneStoreRemovalKind.CanceledPendingAddition;
        }

        if (isExecuting)
            m_pendingComponentRemovals.Add(new PendingComponentRemoval(entry));
        else
            CommitComponentRemoval(entry);
        return SceneStoreRemovalKind.RemovedCommitted;
    }

    internal void ReplaceComponent(GameComponent previous, GameComponent replacement)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(replacement);
        if (isExecuting || hasPendingChanges)
            throw new InvalidOperationException("Components cannot be replaced during a scene execution phase.");
        if (!m_components.TryGetValue(previous, out ComponentEntry? previousEntry) || !previousEntry.isAlive)
            throw new InvalidOperationException("The component being replaced is not attached to this scene.");
        if (m_components.ContainsKey(replacement))
            throw new InvalidOperationException("The replacement component is already attached to this scene.");

        SceneObjectRecord owner = previousEntry.owner;
        int index = owner.components.IndexOf(previous);
        if (index < 0)
            throw new InvalidOperationException("The component attachment order is inconsistent.");
        IComponentBucket replacementBucket = m_buckets.GetOrCreate(replacement.GetType());
        var replacementEntry = new ComponentEntry(owner, replacement, replacementBucket)
        {
            isCommitted = previousEntry.isCommitted
        };
        if (previousEntry.isCommitted)
        {
            previousEntry.bucket.Remove(previous);
            m_buckets.RemoveIfEmpty(previousEntry.bucket);
            replacementBucket.Add(replacement);
        }
        previousEntry.isAlive = false;
        previousEntry.isCommitted = false;
        owner.components[index] = replacement;
        m_components.Remove(previous);
        m_components.Add(replacement, replacementEntry);
        InvalidateQueryCaches();
    }

    internal IReadOnlyList<SceneStoreRemovedComponent> RemoveObject(GameObject gameObject)
    {
        if (!m_records.TryGetValue(gameObject, out SceneObjectRecord? record) || !record.isAlive)
            return Array.Empty<SceneStoreRemovedComponent>();

        record.isAlive = false;
        GameComponent[] attached = [.. record.components];
        var removed = new SceneStoreRemovedComponent[attached.Length];
        for (int i = 0; i < attached.Length; i++)
        {
            bool wasCommitted = m_components.TryGetValue(attached[i], out ComponentEntry? entry) && entry.isCommitted;
            removed[i] = new SceneStoreRemovedComponent(attached[i], wasCommitted);
            RemoveComponentEntry(record, attached[i]);
        }
        record.components.Clear();
        InvalidateQueryCaches();

        if (!record.isCommitted)
        {
            m_records.Remove(gameObject);
            return removed;
        }

        if (isExecuting)
            m_pendingObjectRemovals.Add(new PendingObjectRemoval(record));
        else
            CommitObjectRemoval(record);
        return removed;
    }

    internal bool Contains(GameObject gameObject)
        => m_records.TryGetValue(gameObject, out SceneObjectRecord? record) && record.isAlive;

    internal IReadOnlyList<GameObject> GetObjects()
    {
        if (m_objectSnapshotCache is not null)
            return m_objectSnapshotCache;

        m_objectSnapshotCache = m_committedObjects.All()
            .Where(static record => record.isAlive && record.isCommitted)
            .Select(static record => record.gameObject)
            .ToArray();
        return m_objectSnapshotCache;
    }

    internal GameObject? FindObject(string name)
    {
        EnsureMetadataIndexes();
        return m_firstObjectByName!.GetValueOrDefault(name);
    }

    internal GameObject? FindObjectWithTag(string tag)
    {
        EnsureMetadataIndexes();
        return m_objectsByTag!.TryGetValue(tag, out GameObject[]? matches) && matches.Length != 0
            ? matches[0]
            : null;
    }

    internal IReadOnlyList<GameObject> FindObjectsWithTag(string tag)
    {
        EnsureMetadataIndexes();
        return m_objectsByTag!.TryGetValue(tag, out GameObject[]? matches)
            ? matches
            : Array.Empty<GameObject>();
    }

    internal GameObject? FindObjectWithLayer(GameLayer layer)
    {
        EnsureMetadataIndexes();
        return m_objectsByLayer!.TryGetValue(layer, out GameObject[]? matches) && matches.Length != 0
            ? matches[0]
            : null;
    }

    internal IReadOnlyList<GameObject> FindObjectsWithLayer(GameLayer layer)
    {
        EnsureMetadataIndexes();
        return m_objectsByLayer!.TryGetValue(layer, out GameObject[]? matches)
            ? matches
            : Array.Empty<GameObject>();
    }

    internal IReadOnlyList<GameObject> FindObjectsWithLayers(GameLayerMask layers)
    {
        if (layers == GameLayerMask.none)
            return Array.Empty<GameObject>();
        return GetObjects().Where(gameObject => layers.Contains(gameObject.layer)).ToArray();
    }

    internal void NotifyObjectMetadataChanged(GameObject gameObject)
    {
        _ = GetAliveRecord(gameObject);
        InvalidateMetadataIndexes();
    }

    internal IReadOnlyList<GameObject> GetOwnedObjects()
        => m_records.Values.Where(static record => record.isAlive).Select(static record => record.gameObject).ToArray();

    internal IReadOnlyList<GameComponent> GetComponents(GameObject owner)
        => GetAliveRecord(owner).components.Where(IsLocallyVisible).ToArray();

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

    internal IReadOnlyList<TComponent> GetComponents<TComponent>(GameObject owner)
        where TComponent : GameComponent
        => GetAliveRecord(owner).components.Where(IsLocallyVisible).OfType<TComponent>().ToArray();

    internal IReadOnlyList<TComponent> GetComponents<TComponent>() where TComponent : GameComponent
    {
        Type requestedType = typeof(TComponent);
        if (!m_componentQueryCache.TryGetValue(requestedType, out GameComponent[]? cached))
        {
            cached = m_buckets.GetAssignableTo(requestedType)
                .SelectMany(static bucket => bucket.GetSnapshot())
                .Where(IsVisible)
                .ToArray();
            m_componentQueryCache.Add(requestedType, cached);
        }

        return cached.Cast<TComponent>().ToArray();
    }

    internal IReadOnlyList<GameObject> Query(params Type[] componentTypes)
    {
        ArgumentNullException.ThrowIfNull(componentTypes);
        if (componentTypes.Length == 0)
            return GetObjects();
        for (int i = 0; i < componentTypes.Length; i++)
        {
            Type requestedType = componentTypes[i];
            if (!typeof(GameComponent).IsAssignableFrom(requestedType))
                throw new ArgumentException($"Query type '{requestedType.FullName}' is not a GameComponent.", nameof(componentTypes));
        }

        Type[] normalized = componentTypes.Distinct().OrderBy(static type => type.AssemblyQualifiedName, StringComparer.Ordinal).ToArray();
        string cacheKey = string.Join('|', normalized.Select(static type => type.AssemblyQualifiedName));
        if (m_objectQueryCache.TryGetValue(cacheKey, out GameObject[]? cached))
            return cached;

        IReadOnlyList<GameComponent> candidates = normalized
            .Select(GetCommittedComponents)
            .OrderBy(static components => components.Count)
            .First();
        var seen = new HashSet<GameObject>(ReferenceEqualityComparer.Instance);
        var result = new List<GameObject>();
        for (int i = 0; i < candidates.Count; i++)
        {
            GameComponent candidate = candidates[i];
            if (!m_components.TryGetValue(candidate, out ComponentEntry? entry) ||
                !IsVisible(candidate) ||
                !seen.Add(entry.owner.gameObject))
            {
                continue;
            }

            bool matches = true;
            for (int typeIndex = 0; typeIndex < normalized.Length; typeIndex++)
            {
                Type requiredType = normalized[typeIndex];
                if (!entry.owner.components.Any(component => IsVisible(component) && requiredType.IsInstanceOfType(component)))
                {
                    matches = false;
                    break;
                }
            }

            if (matches)
                result.Add(entry.owner.gameObject);
        }

        cached = result.ToArray();
        m_objectQueryCache.Add(cacheKey, cached);
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

    internal void Clear()
    {
        InvalidateQueryCaches();
        if (isExecuting)
        {
            m_clearRequested = true;
            return;
        }

        ClearImmediately();
    }

    private IReadOnlyList<GameComponent> GetCommittedComponents(Type requestedType)
    {
        if (!m_componentQueryCache.TryGetValue(requestedType, out GameComponent[]? cached))
        {
            cached = m_buckets.GetAssignableTo(requestedType)
                .SelectMany(static bucket => bucket.GetSnapshot())
                .Where(IsVisible)
                .ToArray();
            m_componentQueryCache.Add(requestedType, cached);
        }
        return cached;
    }

    private bool IsVisible(GameComponent component)
        => m_components.TryGetValue(component, out ComponentEntry? entry) &&
           entry.isAlive &&
           entry.isCommitted &&
           entry.owner.isAlive &&
           entry.owner.isCommitted;

    private bool IsLocallyVisible(GameComponent component)
        => m_components.TryGetValue(component, out ComponentEntry? entry) &&
           entry.isAlive &&
           entry.owner.isAlive;

    private SceneObjectRecord GetAliveRecord(GameObject owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        if (!m_records.TryGetValue(owner, out SceneObjectRecord? record) || !record.isAlive)
        {
            throw new InvalidOperationException(
                $"GameObject '{owner.identity.persistentId}' does not belong to this scene store.");
        }
        return record;
    }

    private void RemoveComponentEntry(SceneObjectRecord record, GameComponent component)
    {
        if (!m_components.TryGetValue(component, out ComponentEntry? entry) || !entry.isAlive)
            return;

        entry.isAlive = false;
        record.components.Remove(component);
        if (!entry.isCommitted)
        {
            m_components.Remove(component);
            m_buckets.RemoveIfEmpty(entry.bucket);
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
        m_committedObjects.Add(record);
        InvalidateQueryCaches();
    }

    private void CommitObjectRemoval(SceneObjectRecord record)
    {
        record.isCommitted = false;
        m_committedObjects.Remove(record);
        m_records.Remove(record.gameObject);
        InvalidateQueryCaches();
    }

    private void CommitComponentAddition(ComponentEntry entry)
    {
        entry.bucket.Add(entry.component);
        entry.isCommitted = true;
        InvalidateQueryCaches();
    }

    private void CommitComponentRemoval(ComponentEntry entry)
    {
        entry.bucket.Remove(entry.component);
        m_buckets.RemoveIfEmpty(entry.bucket);
        entry.isCommitted = false;
        m_components.Remove(entry.component);
        InvalidateQueryCaches();
    }

    private void InvalidateQueryCaches()
    {
        m_objectSnapshotCache = null;
        m_componentQueryCache.Clear();
        m_objectQueryCache.Clear();
        InvalidateMetadataIndexes();
    }

    private void EnsureMetadataIndexes()
    {
        if (m_firstObjectByName is not null && m_objectsByTag is not null && m_objectsByLayer is not null)
            return;

        var firstByName = new Dictionary<string, GameObject>(StringComparer.Ordinal);
        var byTag = new Dictionary<string, List<GameObject>>(StringComparer.Ordinal);
        var byLayer = new Dictionary<GameLayer, List<GameObject>>();
        IReadOnlyList<GameObject> objects = GetObjects();
        for (int i = 0; i < objects.Count; i++)
        {
            GameObject gameObject = objects[i];
            firstByName.TryAdd(gameObject.name, gameObject);
            if (!byTag.TryGetValue(gameObject.tag, out List<GameObject>? matches))
            {
                matches = [];
                byTag.Add(gameObject.tag, matches);
            }
            matches.Add(gameObject);
            if (!byLayer.TryGetValue(gameObject.layer, out List<GameObject>? layerMatches))
            {
                layerMatches = [];
                byLayer.Add(gameObject.layer, layerMatches);
            }
            layerMatches.Add(gameObject);
        }

        m_firstObjectByName = firstByName;
        m_objectsByTag = byTag.ToDictionary(
            static pair => pair.Key,
            static pair => pair.Value.ToArray(),
            StringComparer.Ordinal);
        m_objectsByLayer = byLayer.ToDictionary(
            static pair => pair.Key,
            static pair => pair.Value.ToArray());
    }

    private void InvalidateMetadataIndexes()
    {
        m_firstObjectByName = null;
        m_objectsByTag = null;
        m_objectsByLayer = null;
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
        m_components.Clear();
        m_records.Clear();
        m_buckets.Clear();
        m_committedObjects.RemoveAll();
        m_clearRequested = false;
        InvalidateQueryCaches();
    }

    private sealed class ComponentEntry
    {
        internal ComponentEntry(SceneObjectRecord owner, GameComponent component, IComponentBucket bucket)
        {
            this.owner = owner;
            this.component = component;
            this.bucket = bucket;
        }

        internal SceneObjectRecord owner { get; }
        internal GameComponent component { get; }
        internal IComponentBucket bucket { get; }
        internal bool isAlive { get; set; } = true;
        internal bool isCommitted { get; set; }
    }

    private readonly record struct PendingObjectAddition(SceneObjectRecord record);
    private readonly record struct PendingObjectRemoval(SceneObjectRecord record);
    private readonly record struct PendingComponentAddition(ComponentEntry entry);
    private readonly record struct PendingComponentRemoval(ComponentEntry entry);

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
