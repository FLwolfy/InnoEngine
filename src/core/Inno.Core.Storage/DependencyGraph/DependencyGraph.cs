using System;
using System.Collections.Generic;
using System.Threading;

namespace Inno.Core.Storage;

/// <summary>
/// Dependency graph with optional caching.
/// </summary>
/// <remarks>
/// It tracks edges and can store TValue per node. Dirty propagation is supported for
/// incremental recomputation.
/// </remarks>
public sealed class DependencyGraph<TKey, TValue> where TKey : notnull
{
    private readonly Dictionary<TKey, HashSet<TKey>> m_edges = new();
    private readonly Dictionary<TKey, HashSet<TKey>> m_rev = new();
    private readonly Dictionary<TKey, DependencyEntry<TValue>> m_entries = new();
    private readonly Lock m_sync = new();

    /// <summary>
    /// Whether cycles are allowed.
    /// </summary>
    /// <remarks>
    /// When false, TopologicalSort/UpdateDirty throw if a cycle exists. When true, TopologicalSort
    /// returns a partial order and exposes cyclic nodes.
    /// </remarks>
    public bool allowCycles { get; set; }

    /// <summary>
    /// Cache update strategy.
    /// </summary>
    /// <remarks>
    /// Use Disabled for a pure dependency graph with no cached TValue.
    /// </remarks>
    public DependencyCacheMode dependencyCacheMode { get; set; } = DependencyCacheMode.Hybrid;

    /// <summary>
    /// Number of nodes in the graph (edges are not counted).
    /// </summary>
    public int count
    {
        get
        {
            lock (m_sync)
                return m_edges.Count;
        }
    }

    /// <summary>
    /// Adds a node if it does not already exist.
    /// </summary>
    /// <remarks>
    /// No edges are created.
    /// </remarks>
    /// <param name="node">Node key.</param>
    public void AddNode(TKey node)
    {
        lock (m_sync)
        {
            if (!m_edges.ContainsKey(node))
                m_edges[node] = new HashSet<TKey>();

            if (!m_rev.ContainsKey(node))
                m_rev[node] = new HashSet<TKey>();

            if (!m_entries.ContainsKey(node))
                m_entries[node] = new DependencyEntry<TValue>();
        }
    }

    /// <summary>
    /// Adds a dependency edge (node depends on dependsOn).
    /// </summary>
    /// <remarks>
    /// Both nodes are created if missing.
    /// </remarks>
    /// <param name="node">Dependent node.</param>
    /// <param name="dependsOn">Dependency node.</param>
    public void AddDependency(TKey node, TKey dependsOn)
    {
        lock (m_sync)
        {
            AddNode(node);
            AddNode(dependsOn);

            if (!m_edges[node].Add(dependsOn))
                return;

            m_rev[dependsOn].Add(node);
        }
    }

    /// <summary>
    /// Removes a dependency edge.
    /// </summary>
    /// <remarks>
    /// Returns false if the edge did not exist.
    /// </remarks>
    /// <param name="node">Dependent node.</param>
    /// <param name="dependsOn">Dependency node.</param>
    /// <returns>True if removed; otherwise false.</returns>
    public bool RemoveDependency(TKey node, TKey dependsOn)
    {
        lock (m_sync)
        {
            if (!m_edges.TryGetValue(node, out var set))
                return false;

            if (!set.Remove(dependsOn))
                return false;

            if (m_rev.TryGetValue(dependsOn, out var rev))
                rev.Remove(node);

            return true;
        }
    }

    /// <summary>
    /// Removes a node and all incident edges.
    /// </summary>
    /// <remarks>
    /// Cached value (if any) is also removed.
    /// </remarks>
    /// <param name="node">Node key.</param>
    /// <returns>True if removed; otherwise false.</returns>
    public bool RemoveNode(TKey node)
    {
        lock (m_sync)
        {
            if (!m_edges.Remove(node))
                return false;

            if (m_rev.Remove(node, out var revSet))
            {
                foreach (var r in revSet)
                {
                    if (m_edges.TryGetValue(r, out var set))
                        set.Remove(node);
                }
            }

            m_entries.Remove(node);
            return true;
        }
    }

    /// <summary>
    /// Clears all nodes, edges, and cached values.
    /// </summary>
    public void Clear()
    {
        lock (m_sync)
        {
            m_edges.Clear();
            m_rev.Clear();
            m_entries.Clear();
        }
    }

    /// <summary>
    /// Returns a topological ordering.
    /// </summary>
    /// <remarks>
    /// If cycles exist and allowCycles is false, throws. If allowCycles is true, returns a partial
    /// order (acyclic subset).
    /// </remarks>
    /// <returns>Topological order of nodes.</returns>
    public IReadOnlyList<TKey> TopologicalSort()
        => TopologicalSort(out _);

    /// <summary>
    /// Returns a topological ordering and outputs cyclic nodes when cycles exist.
    /// </summary>
    /// <remarks>
    /// When allowCycles is false and cycles exist, throws.
    /// </remarks>
    /// <param name="cyclicNodes">Nodes that are part of cycles.</param>
    /// <returns>Topological order of acyclic nodes.</returns>
    public IReadOnlyList<TKey> TopologicalSort(out IReadOnlyList<TKey> cyclicNodes)
    {
        var snapshot = Snapshot();

        var result = new List<TKey>(snapshot.Count);
        var incoming = new Dictionary<TKey, int>(snapshot.Count);
        foreach (var node in snapshot.Keys)
            incoming[node] = 0;

        foreach (var kv in snapshot)
        {
            foreach (var dep in kv.Value)
                incoming[kv.Key] = incoming[kv.Key] + 1;
        }

        var queue = new Queue<TKey>();
        foreach (var kv in incoming)
        {
            if (kv.Value == 0)
                queue.Enqueue(kv.Key);
        }

        while (queue.Count > 0)
        {
            var n = queue.Dequeue();
            result.Add(n);

            foreach (var kv in snapshot)
            {
                if (!kv.Value.Remove(n))
                    continue;

                incoming[kv.Key] = incoming[kv.Key] - 1;
                if (incoming[kv.Key] == 0)
                    queue.Enqueue(kv.Key);
            }
        }

        if (result.Count == snapshot.Count)
        {
            cyclicNodes = Array.Empty<TKey>();
            return result;
        }

        var leftovers = new List<TKey>();
        foreach (var kv in incoming)
        {
            if (kv.Value > 0)
                leftovers.Add(kv.Key);
        }

        cyclicNodes = leftovers;

        if (!allowCycles)
            throw new InvalidOperationException("Dependency graph contains cycles.");

        return result;
    }

    /// <summary>
    /// Returns true if the graph contains a cycle.
    /// </summary>
    /// <returns>True if a cycle exists.</returns>
    public bool HasCycle()
    {
        var snapshot = Snapshot();
        var state = new Dictionary<TKey, int>(snapshot.Count);
        var stack = new Stack<TKey>();
        var indexByNode = new Dictionary<TKey, int>(snapshot.Count);

        foreach (var node in snapshot.Keys)
            state[node] = 0;

        foreach (var node in snapshot.Keys)
        {
            if (state[node] != 0)
                continue;

            if (Dfs(node, snapshot, state, stack, indexByNode, out _))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Marks a node as dirty and propagates to dependents (reverse edges).
    /// </summary>
    /// <remarks>
    /// No-op when dependencyCacheMode is Disabled.
    /// </remarks>
    /// <param name="key">Node key.</param>
    public void Invalidate(TKey key)
    {
        if (dependencyCacheMode == DependencyCacheMode.Disabled)
            return;

        List<TKey> toVisit;
        lock (m_sync)
        {
            if (!m_entries.TryGetValue(key, out var entry))
                return;

            entry.dirty = true;
            entry.generation++;

            toVisit = new List<TKey>();
            if (m_rev.TryGetValue(key, out var rev))
                toVisit.AddRange(rev);
        }

        if (toVisit.Count == 0)
            return;

        var queue = new Queue<TKey>(toVisit);
        var visited = new HashSet<TKey>();

        while (queue.Count > 0)
        {
            var n = queue.Dequeue();
            if (!visited.Add(n))
                continue;

            lock (m_sync)
            {
                if (m_entries.TryGetValue(n, out var entry))
                {
                    entry.dirty = true;
                    entry.generation++;
                }

                if (m_rev.TryGetValue(n, out var rev))
                {
                    foreach (var r in rev)
                        queue.Enqueue(r);
                }
            }
        }
    }

    /// <summary>
    /// Tries to get a cached value only if it exists and is not dirty.
    /// </summary>
    /// <remarks>
    /// Returns false if dependencyCacheMode is Disabled or the value is missing/stale.
    /// </remarks>
    /// <param name="key">Node key.</param>
    /// <param name="value">Cached value, if available.</param>
    /// <returns>True if a valid cached value exists.</returns>
    public bool TryGet(TKey key, out TValue? value)
    {
        value = default;
        if (dependencyCacheMode == DependencyCacheMode.Disabled)
            return false;

        lock (m_sync)
        {
            if (!m_entries.TryGetValue(key, out var entry) || !entry.hasValue || entry.dirty)
                return false;

            entry.lastAccessTicks = Environment.TickCount64;
            value = entry.value;
            return true;
        }
    }

    /// <summary>
    /// Gets a cached value or computes it using factory(key) and stores the result.
    /// </summary>
    /// <remarks>
    /// In Eager mode, this first updates all dirty nodes. In Disabled mode, it just returns factory(key).
    /// </remarks>
    /// <param name="key">Node key.</param>
    /// <param name="factory">Factory used to compute the value.</param>
    /// <returns>The cached or computed value.</returns>
    public TValue GetOrUpdate(TKey key, Func<TKey, TValue> factory)
    {
        if (factory == null) throw new ArgumentNullException(nameof(factory));

        if (dependencyCacheMode == DependencyCacheMode.Disabled)
            return factory(key);

        if (dependencyCacheMode == DependencyCacheMode.Eager)
            UpdateDirty(factory);

        int generation;

        lock (m_sync)
        {
            AddNode(key);
            var entry = m_entries[key];

            if (entry.hasValue && !entry.dirty)
            {
                entry.lastAccessTicks = Environment.TickCount64;
                return entry.value!;
            }

            generation = entry.generation;
        }

        var computed = factory(key);

        lock (m_sync)
        {
            var entry = m_entries[key];
            if (entry.generation != generation)
                return entry.hasValue ? entry.value! : computed;

            entry.value = computed;
            entry.hasValue = true;
            entry.dirty = false;
            entry.lastAccessTicks = Environment.TickCount64;
            entry.lastUpdateTicks = entry.lastAccessTicks;
            return computed;
        }
    }

    /// <summary>
    /// Updates dirty nodes in dependency order using factory(key). Returns how many nodes were updated.
    /// </summary>
    /// <remarks>
    /// If allowCycles is true, cyclic nodes are updated after the acyclic subset.
    /// </remarks>
    /// <param name="factory">Factory used to compute values.</param>
    /// <param name="maxCount">Maximum number of nodes to update.</param>
    /// <returns>Number of nodes updated.</returns>
    public int UpdateDirty(Func<TKey, TValue> factory, int maxCount = int.MaxValue)
    {
        if (factory == null) throw new ArgumentNullException(nameof(factory));
        if (dependencyCacheMode == DependencyCacheMode.Disabled)
            return 0;

        var snapshot = Snapshot();
        var order = TopologicalOrder(snapshot, out var cyclic);

        if (cyclic.Count > 0 && !allowCycles)
            throw new InvalidOperationException("Dependency graph contains cycles.");

        int updated = 0;
        for (int i = 0; i < order.Count && updated < maxCount; i++)
        {
            var key = order[i];
            if (!IsDirty(key, out var gen))
                continue;

            var value = factory(key);
            if (TryCommit(key, gen, value))
                updated++;
        }

        if (allowCycles && cyclic.Count > 0 && updated < maxCount)
        {
            for (int i = 0; i < cyclic.Count && updated < maxCount; i++)
            {
                var key = cyclic[i];
                if (!IsDirty(key, out var gen))
                    continue;

                var value = factory(key);
                if (TryCommit(key, gen, value))
                    updated++;
            }
        }

        return updated;
    }

    private bool IsDirty(TKey key, out int generation)
    {
        lock (m_sync)
        {
            if (!m_entries.TryGetValue(key, out var entry))
            {
                generation = 0;
                return false;
            }

            generation = entry.generation;
            return entry.dirty;
        }
    }

    private bool TryCommit(TKey key, int generation, TValue value)
    {
        lock (m_sync)
        {
            if (!m_entries.TryGetValue(key, out var entry))
                return false;

            if (entry.generation != generation)
                return false;

            entry.value = value;
            entry.hasValue = true;
            entry.dirty = false;
            entry.lastAccessTicks = Environment.TickCount64;
            entry.lastUpdateTicks = entry.lastAccessTicks;
            return true;
        }
    }

    private Dictionary<TKey, HashSet<TKey>> Snapshot()
    {
        lock (m_sync)
        {
            var snapshot = new Dictionary<TKey, HashSet<TKey>>(m_edges.Count);
            foreach (var kv in m_edges)
                snapshot[kv.Key] = new HashSet<TKey>(kv.Value);
            return snapshot;
        }
    }

    private static List<TKey> TopologicalOrder(Dictionary<TKey, HashSet<TKey>> deps, out List<TKey> cyclic)
    {
        var incoming = new Dictionary<TKey, int>(deps.Count);
        foreach (var node in deps.Keys)
            incoming[node] = 0;

        foreach (var kv in deps)
        {
            foreach (var dep in kv.Value)
                incoming[kv.Key] = incoming[kv.Key] + 1;
        }

        var queue = new Queue<TKey>();
        foreach (var kv in incoming)
        {
            if (kv.Value == 0)
                queue.Enqueue(kv.Key);
        }

        var result = new List<TKey>(deps.Count);
        var remaining = new Dictionary<TKey, HashSet<TKey>>(deps.Count);
        foreach (var kv in deps)
            remaining[kv.Key] = new HashSet<TKey>(kv.Value);

        while (queue.Count > 0)
        {
            var n = queue.Dequeue();
            result.Add(n);

            foreach (var kv in remaining)
            {
                if (!kv.Value.Remove(n))
                    continue;

                incoming[kv.Key] = incoming[kv.Key] - 1;
                if (incoming[kv.Key] == 0)
                    queue.Enqueue(kv.Key);
            }
        }

        cyclic = new List<TKey>();
        if (result.Count != deps.Count)
        {
            foreach (var kv in incoming)
            {
                if (kv.Value > 0)
                    cyclic.Add(kv.Key);
            }
        }

        return result;
    }

    private static bool Dfs(
        TKey node,
        Dictionary<TKey, HashSet<TKey>> edges,
        Dictionary<TKey, int> state,
        Stack<TKey> stack,
        Dictionary<TKey, int> indexByNode,
        out IReadOnlyList<TKey> cycle)
    {
        state[node] = 1;
        indexByNode[node] = stack.Count;
        stack.Push(node);

        if (edges.TryGetValue(node, out var deps))
        {
            foreach (var dep in deps)
            {
                if (!state.TryGetValue(dep, out var s))
                {
                    state[dep] = 0;
                    s = 0;
                }

                if (s == 0)
                {
                    if (Dfs(dep, edges, state, stack, indexByNode, out cycle))
                        return true;
                }
                else if (s == 1)
                {
                    if (!indexByNode.TryGetValue(dep, out var start))
                        start = 0;

                    var arr = stack.ToArray();
                    Array.Reverse(arr);

                    var list = new List<TKey>();
                    for (int i = start; i < arr.Length; i++)
                        list.Add(arr[i]);

                    list.Add(dep);
                    cycle = list;
                    return true;
                }
            }
        }

        stack.Pop();
        indexByNode.Remove(node);
        state[node] = 2;

        cycle = Array.Empty<TKey>();
        return false;
    }
}
