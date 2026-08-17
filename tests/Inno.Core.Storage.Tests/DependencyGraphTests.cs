using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Inno.Core.Storage;

using Xunit;

namespace Inno.Core.Storage.Tests;

public sealed class DependencyGraphTests
{
    [Fact]
    public void NodesAndEdges_AreIdempotentAndVersionOnlyTracksChanges()
    {
        var graph = new DependencyGraph<string>();
        Assert.True(graph.AddNode("A"));
        Assert.False(graph.AddNode("A"));
        Assert.Equal(1, graph.version);
        Assert.True(graph.AddDependency("B", "A"));
        Assert.False(graph.AddDependency("B", "A"));
        Assert.Equal(2, graph.count);
        Assert.Equal(2, graph.version);
        Assert.True(graph.ContainsNode("A"));
        Assert.Equal(new[] { "A" }, graph.GetDependencies("B"));
    }

    [Fact]
    public void ReplaceDependencies_UpdatesForwardAndReverseEdgesAtomically()
    {
        var graph = new DependencyGraph<string>();
        graph.ReplaceDependencies("Root", ["A", "B"]);
        long version = graph.version;
        graph.ReplaceDependencies("Root", ["B", "C"]);
        Assert.Equal(version + 1, graph.version);
        Assert.Equal(new[] { "B", "C" }, graph.GetDependencies("Root"));
        Assert.Empty(graph.GetDependents("A"));
        Assert.Equal(new[] { "Root" }, graph.GetDependents("B"));
        Assert.Equal(new[] { "Root" }, graph.GetDependents("C"));
        graph.ReplaceDependencies("Root", ["C", "B"]);
        Assert.Equal(version + 1, graph.version);
    }

    [Fact]
    public void RemoveNode_RemovesAllIncomingAndOutgoingEdges()
    {
        var graph = new DependencyGraph<string>();
        graph.AddDependency("C", "B");
        graph.AddDependency("B", "A");
        graph.AddDependency("D", "B");
        Assert.True(graph.RemoveNode("B"));
        Assert.False(graph.ContainsNode("B"));
        Assert.Empty(graph.GetDependencies("C"));
        Assert.Empty(graph.GetDependencies("D"));
        Assert.Empty(graph.GetDependents("A"));
        Assert.False(graph.RemoveNode("B"));
    }

    [Fact]
    public void RecursiveQueries_ReturnDeterministicTransitiveSnapshots()
    {
        var graph = new DependencyGraph<string>();
        graph.AddDependency("D", "B");
        graph.AddDependency("D", "C");
        graph.AddDependency("B", "A");
        graph.AddDependency("C", "A");
        Assert.Equal(new[] { "B", "C" }, graph.GetDependencies("D"));
        Assert.Equal(new[] { "B", "C", "A" }, graph.GetDependencies("D", recursive: true));
        Assert.Equal(new[] { "D", "B", "C" }, graph.GetDependents("A", recursive: true));
        Assert.True(graph.DependsOn("D", "A", recursive: true));
        Assert.False(graph.DependsOn("D", "A"));
        Assert.Empty(graph.GetDependencies("Missing"));
    }

    [Fact]
    public void TopologicalSort_PlacesEveryDependencyBeforeItsDependent()
    {
        var graph = new DependencyGraph<string>();
        graph.AddDependency("C", "B");
        graph.AddDependency("B", "A");
        graph.AddNode("D");
        IReadOnlyList<string> order = graph.TopologicalSort();
        Assert.True(IndexOf(order, "A") < IndexOf(order, "B"));
        Assert.True(IndexOf(order, "B") < IndexOf(order, "C"));
        Assert.Equal(new[] { "A", "B", "C", "D" }, order);
    }

    [Fact]
    public void OrderingComparer_ControlsStableQueryOrder()
    {
        var graph = new DependencyGraph<string>(orderingComparer: StringComparer.Ordinal);
        graph.ReplaceDependencies("Root", ["Z", "A", "M"]);
        Assert.Equal(new[] { "A", "M", "Z" }, graph.GetDependencies("Root"));
    }

    [Fact]
    public void CycleDetection_ReturnsClosedCompleteCycleAndTopologicalSortReportsIt()
    {
        var graph = new DependencyGraph<string>();
        graph.AddDependency("A", "B");
        graph.AddDependency("B", "C");
        graph.AddDependency("C", "A");
        Assert.True(graph.TryFindCycle(out IReadOnlyList<string> cycle));
        Assert.Equal(cycle[0], cycle[^1]);
        Assert.Equal(new[] { "A", "B", "C" }, cycle.Take(cycle.Count - 1).OrderBy(static x => x));
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => graph.TopologicalSort());
        Assert.Contains("A", exception.Message);
        Assert.Contains("B", exception.Message);
        Assert.Contains("C", exception.Message);
        Assert.Contains("->", exception.Message);
    }

    [Fact]
    public void SelfCycle_IsDetected()
    {
        var graph = new DependencyGraph<int>();
        graph.AddDependency(7, 7);
        Assert.True(graph.TryFindCycle(out IReadOnlyList<int> cycle));
        Assert.Equal(new[] { 7, 7 }, cycle);
    }

    [Fact]
    public void StronglyConnectedComponents_IncludeCyclesAndSingletons()
    {
        var graph = new DependencyGraph<string>();
        graph.AddDependency("A", "B");
        graph.AddDependency("B", "A");
        graph.AddDependency("C", "B");
        graph.AddNode("D");
        string[] components = graph.GetStronglyConnectedComponents()
            .Select(static component => string.Join(",", component))
            .ToArray();
        Assert.Equal(new[] { "A,B", "C", "D" }, components);
    }

    [Fact]
    public void Clear_RemovesAllStateAndOnlyChangesVersionWhenNeeded()
    {
        var graph = new DependencyGraph<int>();
        graph.AddDependency(2, 1);
        long beforeClear = graph.version;
        graph.Clear();
        Assert.Equal(0, graph.count);
        Assert.Equal(beforeClear + 1, graph.version);
        graph.Clear();
        Assert.Equal(beforeClear + 1, graph.version);
    }

    [Fact]
    public async Task ConcurrentReaders_ObserveOnlyCompleteSnapshots()
    {
        var graph = new DependencyGraph<int>();
        graph.ReplaceDependencies(10, [1, 2, 3]);
        Task[] readers = Enumerable.Range(0, 8)
            .Select(_ => Task.Run(() =>
            {
                for (int i = 0; i < 500; i++)
                {
                    IReadOnlyList<int> dependencies = graph.GetDependencies(10);
                    Assert.True(
                        dependencies.SequenceEqual(new[] { 1, 2, 3 }) ||
                        dependencies.SequenceEqual(new[] { 4, 5, 6 }));
                }
            }))
            .ToArray();
        for (int i = 0; i < 100; i++)
            graph.ReplaceDependencies(10, i % 2 == 0 ? [4, 5, 6] : [1, 2, 3]);
        await Task.WhenAll(readers);
    }

    private static int IndexOf(IReadOnlyList<string> values, string value)
    {
        for (int i = 0; i < values.Count; i++)
        {
            if (values[i] == value)
                return i;
        }
        return -1;
    }
}
