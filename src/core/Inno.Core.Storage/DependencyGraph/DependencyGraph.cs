using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Inno.Core.Storage;

/// <summary>
/// Stores a directed dependency graph and provides deterministic dependency queries.
/// </summary>
/// <typeparam name="TKey">
/// The node key type.
/// </typeparam>
/// <remarks>
/// An edge from <c>node</c> to <c>dependency</c> means that <c>node</c> depends on
/// <c>dependency</c>. The graph accepts cycles; operations that require an acyclic graph,
/// such as <see cref="TopologicalSort"/>, report them explicitly.
/// </remarks>
public sealed class DependencyGraph<TKey> where TKey : notnull
{
    private sealed class Node
    {
        internal required long order;
        internal HashSet<TKey> dependencies = null!;
        internal HashSet<TKey> dependents = null!;
    }

    private sealed class NodePriorityComparer(
        IComparer<TKey>? orderingComparer) : IComparer<NodePriority>
    {
        /// <summary>
        /// Compares two values according to the deterministic ordering used by this collection.
        /// </summary>
        /// <param name="x">
        /// The horizontal or first component.
        /// </param>
        /// <param name="y">
        /// The vertical or second component.
        /// </param>
        /// <returns>
        /// The scalar result calculated from the supplied inputs.
        /// </returns>
        public int Compare(NodePriority x, NodePriority y)
        {
            if (orderingComparer is not null)
            {
                int keyComparison = orderingComparer.Compare(x.key, y.key);
                if (keyComparison != 0)
                    return keyComparison;
            }

            return x.order.CompareTo(y.order);
        }
    }

    private readonly struct NodePriority(TKey key, long order)
    {
        internal TKey key { get; } = key;
        internal long order { get; } = order;
    }

    private readonly Dictionary<TKey, Node> m_nodes;
    private readonly IEqualityComparer<TKey> m_equalityComparer;
    private readonly IComparer<TKey>? m_orderingComparer;
    private readonly ReaderWriterLockSlim m_sync = new(LockRecursionPolicy.NoRecursion);

    private long m_nextNodeOrder;
    private long m_version;

    /// <summary>
    /// Creates an empty dependency graph.
    /// </summary>
    /// <param name="equalityComparer">
    /// Optional node equality comparer.
    /// </param>
    /// <param name="orderingComparer">
    /// Optional ordering comparer used to make query results deterministic. When omitted,
    /// node insertion order is used.
    /// </param>
    public DependencyGraph(
        IEqualityComparer<TKey>? equalityComparer = null,
        IComparer<TKey>? orderingComparer = null)
    {
        m_equalityComparer = equalityComparer ?? EqualityComparer<TKey>.Default;
        m_orderingComparer = orderingComparer;
        m_nodes = new Dictionary<TKey, Node>(m_equalityComparer);
    }

    /// <summary>
    /// Gets the number of nodes in the graph.
    /// </summary>
    public int count
    {
        get
        {
            m_sync.EnterReadLock();
            try
            {
                return m_nodes.Count;
            }
            finally
            {
                m_sync.ExitReadLock();
            }
        }
    }

    /// <summary>
    /// Gets the structural version of the graph.
    /// </summary>
    /// <remarks>
    /// The version changes after every successful structural mutation.
    /// </remarks>
    public long version
    {
        get
        {
            m_sync.EnterReadLock();
            try
            {
                return m_version;
            }
            finally
            {
                m_sync.ExitReadLock();
            }
        }
    }

    /// <summary>
    /// Determines whether a node exists.
    /// </summary>
    /// <param name="node">
    /// The node key.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the node exists.
    /// </returns>
    public bool ContainsNode(TKey node)
    {
        m_sync.EnterReadLock();
        try
        {
            return m_nodes.ContainsKey(node);
        }
        finally
        {
            m_sync.ExitReadLock();
        }
    }

    /// <summary>
    /// Adds a node when it does not already exist.
    /// </summary>
    /// <param name="node">
    /// The node key.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when a node was added.
    /// </returns>
    public bool AddNode(TKey node)
    {
        m_sync.EnterWriteLock();
        try
        {
            if (!AddNodeLocked(node))
                return false;

            m_version++;
            return true;
        }
        finally
        {
            m_sync.ExitWriteLock();
        }
    }

    /// <summary>
    /// Removes a node and every incoming and outgoing edge connected to it.
    /// </summary>
    /// <param name="node">
    /// The node key.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the node was removed.
    /// </returns>
    public bool RemoveNode(TKey node)
    {
        m_sync.EnterWriteLock();
        try
        {
            if (!m_nodes.Remove(node, out Node? removed))
                return false;

            foreach (TKey dependency in removed.dependencies)
            {
                if (m_nodes.TryGetValue(dependency, out Node? dependencyNode))
                    dependencyNode.dependents.Remove(node);
            }

            foreach (TKey dependent in removed.dependents)
            {
                if (m_nodes.TryGetValue(dependent, out Node? dependentNode))
                    dependentNode.dependencies.Remove(node);
            }

            m_version++;
            return true;
        }
        finally
        {
            m_sync.ExitWriteLock();
        }
    }

    /// <summary>
    /// Adds an edge indicating that one node depends on another.
    /// </summary>
    /// <param name="node">
    /// The dependent node.
    /// </param>
    /// <param name="dependency">
    /// The required dependency.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the edge was added.
    /// </returns>
    public bool AddDependency(TKey node, TKey dependency)
    {
        m_sync.EnterWriteLock();
        try
        {
            AddNodeLocked(node);
            AddNodeLocked(dependency);
            if (!m_nodes[node].dependencies.Add(dependency))
                return false;

            m_nodes[dependency].dependents.Add(node);
            m_version++;
            return true;
        }
        finally
        {
            m_sync.ExitWriteLock();
        }
    }

    /// <summary>
    /// Removes a dependency edge.
    /// </summary>
    /// <param name="node">
    /// The dependent node.
    /// </param>
    /// <param name="dependency">
    /// The required dependency.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the edge was removed.
    /// </returns>
    public bool RemoveDependency(TKey node, TKey dependency)
    {
        m_sync.EnterWriteLock();
        try
        {
            if (!m_nodes.TryGetValue(node, out Node? dependentNode) ||
                !dependentNode.dependencies.Remove(dependency))
            {
                return false;
            }

            if (m_nodes.TryGetValue(dependency, out Node? dependencyNode))
                dependencyNode.dependents.Remove(node);

            m_version++;
            return true;
        }
        finally
        {
            m_sync.ExitWriteLock();
        }
    }

    /// <summary>
    /// Atomically replaces every direct dependency of a node.
    /// </summary>
    /// <param name="node">
    /// The dependent node.
    /// </param>
    /// <param name="dependencies">
    /// The complete replacement dependency set.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="dependencies"/> is <see langword="null"/>.
    /// </exception>
    public void ReplaceDependencies(TKey node, IEnumerable<TKey> dependencies)
    {
        ArgumentNullException.ThrowIfNull(dependencies);
        var replacement = new HashSet<TKey>(dependencies, m_equalityComparer);

        m_sync.EnterWriteLock();
        try
        {
            bool changed = AddNodeLocked(node);
            foreach (TKey dependency in replacement)
                changed |= AddNodeLocked(dependency);

            Node target = m_nodes[node];
            if (!changed && target.dependencies.SetEquals(replacement))
                return;

            foreach (TKey previous in target.dependencies)
                m_nodes[previous].dependents.Remove(node);

            target.dependencies.Clear();
            foreach (TKey dependency in replacement)
            {
                target.dependencies.Add(dependency);
                m_nodes[dependency].dependents.Add(node);
            }

            m_version++;
        }
        finally
        {
            m_sync.ExitWriteLock();
        }
    }

    /// <summary>
    /// Gets direct or transitive dependencies of a node.
    /// </summary>
    /// <param name="node">
    /// The node to query.
    /// </param>
    /// <param name="recursive">
    /// Whether transitive dependencies should be included.
    /// </param>
    /// <returns>
    /// A stable dependency snapshot, or an empty list when the node is absent.
    /// </returns>
    public IReadOnlyList<TKey> GetDependencies(TKey node, bool recursive = false)
        => GetConnectedNodes(node, recursive, static value => value.dependencies);

    /// <summary>
    /// Gets direct or transitive dependents of a node.
    /// </summary>
    /// <param name="node">
    /// The node to query.
    /// </param>
    /// <param name="recursive">
    /// Whether transitive dependents should be included.
    /// </param>
    /// <returns>
    /// A stable dependent snapshot, or an empty list when the node is absent.
    /// </returns>
    public IReadOnlyList<TKey> GetDependents(TKey node, bool recursive = false)
        => GetConnectedNodes(node, recursive, static value => value.dependents);

    /// <summary>
    /// Determines whether one node directly or transitively depends on another.
    /// </summary>
    /// <param name="node">
    /// The dependent node.
    /// </param>
    /// <param name="dependency">
    /// The dependency to find.
    /// </param>
    /// <param name="recursive">
    /// Whether transitive edges should be searched.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the dependency exists.
    /// </returns>
    public bool DependsOn(TKey node, TKey dependency, bool recursive = false)
    {
        m_sync.EnterReadLock();
        try
        {
            if (!m_nodes.TryGetValue(node, out Node? start))
                return false;
            if (!recursive)
                return start.dependencies.Contains(dependency);

            var visited = new HashSet<TKey>(m_equalityComparer);
            var pending = new Stack<TKey>(start.dependencies);
            while (pending.Count > 0)
            {
                TKey current = pending.Pop();
                if (m_equalityComparer.Equals(current, dependency))
                    return true;
                if (!visited.Add(current) || !m_nodes.TryGetValue(current, out Node? currentNode))
                    continue;
                foreach (TKey next in currentNode.dependencies)
                    pending.Push(next);
            }

            return false;
        }
        finally
        {
            m_sync.ExitReadLock();
        }
    }

    /// <summary>
    /// Returns an order in which every dependency precedes its dependents.
    /// </summary>
    /// <returns>
    /// A deterministic topological ordering.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the graph contains a cycle. The exception message contains a complete cycle.
    /// </exception>
    public IReadOnlyList<TKey> TopologicalSort()
    {
        m_sync.EnterReadLock();
        try
        {
            var remaining = new Dictionary<TKey, int>(m_nodes.Count, m_equalityComparer);
            var ready = new PriorityQueue<TKey, NodePriority>(new NodePriorityComparer(m_orderingComparer));
            foreach ((TKey key, Node value) in m_nodes)
            {
                remaining.Add(key, value.dependencies.Count);
                if (value.dependencies.Count == 0)
                    ready.Enqueue(key, new NodePriority(key, value.order));
            }

            var result = new List<TKey>(m_nodes.Count);
            while (ready.TryDequeue(out TKey? current, out _))
            {
                result.Add(current);
                foreach (TKey dependent in OrderNodes(m_nodes[current].dependents))
                {
                    if (--remaining[dependent] != 0)
                        continue;
                    Node dependentNode = m_nodes[dependent];
                    ready.Enqueue(dependent, new NodePriority(dependent, dependentNode.order));
                }
            }

            if (result.Count == m_nodes.Count)
                return result.ToArray();

            IReadOnlyList<TKey> cycle = FindCycleLocked();
            throw new InvalidOperationException(
                $"Dependency graph contains a cycle: {string.Join(" -> ", cycle)}.");
        }
        finally
        {
            m_sync.ExitReadLock();
        }
    }

    /// <summary>
    /// Tries to find one complete cycle in the graph.
    /// </summary>
    /// <param name="cycle">
    /// A closed cycle whose first and last nodes are equal, or an empty list when acyclic.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when a cycle was found.
    /// </returns>
    public bool TryFindCycle(out IReadOnlyList<TKey> cycle)
    {
        m_sync.EnterReadLock();
        try
        {
            cycle = FindCycleLocked();
            return cycle.Count != 0;
        }
        finally
        {
            m_sync.ExitReadLock();
        }
    }

    /// <summary>
    /// Gets every strongly connected component in deterministic order.
    /// </summary>
    /// <returns>
    /// A stable snapshot containing cyclic components and single-node acyclic components.
    /// </returns>
    public IReadOnlyList<IReadOnlyList<TKey>> GetStronglyConnectedComponents()
    {
        m_sync.EnterReadLock();
        try
        {
            int nextIndex = 0;
            var indexes = new Dictionary<TKey, int>(m_nodes.Count, m_equalityComparer);
            var lowLinks = new Dictionary<TKey, int>(m_nodes.Count, m_equalityComparer);
            var active = new HashSet<TKey>(m_equalityComparer);
            var stack = new Stack<TKey>();
            var components = new List<IReadOnlyList<TKey>>();

            foreach (TKey node in OrderNodes(m_nodes.Keys))
            {
                if (!indexes.ContainsKey(node))
                    Visit(node);
            }

            components.Sort((left, right) => CompareNodes(left[0], right[0]));
            return components.ToArray();

            void Visit(TKey node)
            {
                indexes[node] = nextIndex;
                lowLinks[node] = nextIndex;
                nextIndex++;
                stack.Push(node);
                active.Add(node);

                foreach (TKey dependency in OrderNodes(m_nodes[node].dependencies))
                {
                    if (!indexes.ContainsKey(dependency))
                    {
                        Visit(dependency);
                        lowLinks[node] = Math.Min(lowLinks[node], lowLinks[dependency]);
                    }
                    else if (active.Contains(dependency))
                    {
                        lowLinks[node] = Math.Min(lowLinks[node], indexes[dependency]);
                    }
                }

                if (lowLinks[node] != indexes[node])
                    return;

                var component = new List<TKey>();
                TKey current;
                do
                {
                    current = stack.Pop();
                    active.Remove(current);
                    component.Add(current);
                }
                while (!m_equalityComparer.Equals(current, node));

                component.Sort(CompareNodes);
                components.Add(component.ToArray());
            }
        }
        finally
        {
            m_sync.ExitReadLock();
        }
    }

    /// <summary>
    /// Removes every node and edge.
    /// </summary>
    public void Clear()
    {
        m_sync.EnterWriteLock();
        try
        {
            if (m_nodes.Count == 0)
                return;
            m_nodes.Clear();
            m_version++;
        }
        finally
        {
            m_sync.ExitWriteLock();
        }
    }

    private bool AddNodeLocked(TKey node)
    {
        if (m_nodes.ContainsKey(node))
            return false;
        m_nodes.Add(node, new Node
        {
            order = m_nextNodeOrder++,
            dependencies = new HashSet<TKey>(m_equalityComparer),
            dependents = new HashSet<TKey>(m_equalityComparer)
        });
        return true;
    }

    private IReadOnlyList<TKey> GetConnectedNodes(
        TKey node,
        bool recursive,
        Func<Node, HashSet<TKey>> selector)
    {
        m_sync.EnterReadLock();
        try
        {
            if (!m_nodes.TryGetValue(node, out Node? start))
                return Array.Empty<TKey>();
            if (!recursive)
                return OrderNodes(selector(start));

            var result = new HashSet<TKey>(m_equalityComparer);
            var pending = new Stack<TKey>(selector(start));
            while (pending.Count > 0)
            {
                TKey current = pending.Pop();
                if (!result.Add(current) || !m_nodes.TryGetValue(current, out Node? currentNode))
                    continue;
                foreach (TKey connected in selector(currentNode))
                    pending.Push(connected);
            }

            result.Remove(node);
            return OrderNodes(result);
        }
        finally
        {
            m_sync.ExitReadLock();
        }
    }

    private IReadOnlyList<TKey> FindCycleLocked()
    {
        var states = new Dictionary<TKey, byte>(m_nodes.Count, m_equalityComparer);
        var path = new List<TKey>();
        var pathIndexes = new Dictionary<TKey, int>(m_nodes.Count, m_equalityComparer);

        foreach (TKey node in OrderNodes(m_nodes.Keys))
        {
            if (!states.ContainsKey(node) && Visit(node, out IReadOnlyList<TKey>? cycle))
                return cycle;
        }
        return Array.Empty<TKey>();

        bool Visit(TKey node, out IReadOnlyList<TKey> cycle)
        {
            states[node] = 1;
            pathIndexes[node] = path.Count;
            path.Add(node);
            foreach (TKey dependency in OrderNodes(m_nodes[node].dependencies))
            {
                if (!states.TryGetValue(dependency, out byte state))
                {
                    if (Visit(dependency, out cycle))
                        return true;
                }
                else if (state == 1)
                {
                    int start = pathIndexes[dependency];
                    var found = new List<TKey>(path.Count - start + 1);
                    for (int i = start; i < path.Count; i++)
                        found.Add(path[i]);
                    found.Add(dependency);
                    cycle = found.ToArray();
                    return true;
                }
            }

            states[node] = 2;
            pathIndexes.Remove(node);
            path.RemoveAt(path.Count - 1);
            cycle = Array.Empty<TKey>();
            return false;
        }
    }

    private TKey[] OrderNodes(IEnumerable<TKey> nodes)
    {
        TKey[] result = nodes.ToArray();
        Array.Sort(result, CompareNodes);
        return result;
    }

    private int CompareNodes(TKey left, TKey right)
    {
        if (m_orderingComparer is not null)
        {
            int comparison = m_orderingComparer.Compare(left, right);
            if (comparison != 0)
                return comparison;
        }
        return m_nodes[left].order.CompareTo(m_nodes[right].order);
    }
}
