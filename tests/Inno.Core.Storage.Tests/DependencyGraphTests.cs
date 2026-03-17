using System;
using System.Collections.Generic;
using Inno.Core.Storage;

using Xunit;

namespace Inno.Core.Storage.Tests;

public sealed class DependencyGraphTests
{
    [Fact]
    public void AddDependency_TopologicalSort_RespectsOrder()
    {
        var graph = new DependencyGraph<string, int>();
        graph.AddDependency("C", "B");
        graph.AddDependency("B", "A");

        var order = graph.TopologicalSort();
        var indexA = IndexOf(order, "A");
        var indexB = IndexOf(order, "B");
        var indexC = IndexOf(order, "C");

        Assert.True(indexA < indexB);
        Assert.True(indexB < indexC);
    }

    private static int IndexOf(IReadOnlyList<string> list, string value)
    {
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i] == value)
                return i;
        }

        return -1;
    }

    [Fact]
    public void CycleDetection_ThrowsWhenDisallowed()
    {
        var graph = new DependencyGraph<int, int>();
        graph.AddDependency(1, 2);
        graph.AddDependency(2, 1);

        Assert.Throws<InvalidOperationException>(() => graph.TopologicalSort());
    }

    [Fact]
    public void CycleDetection_AllowsWhenEnabled()
    {
        var graph = new DependencyGraph<int, int> { allowCycles = true };
        graph.AddDependency(1, 2);
        graph.AddDependency(2, 1);

        var order = graph.TopologicalSort(out var cyclic);
        Assert.NotNull(order);
        Assert.True(cyclic.Count > 0);
    }

    [Fact]
    public void Invalidate_PropagatesDirty()
    {
        var graph = new DependencyGraph<string, int>();
        graph.AddDependency("Texture", "File");
        graph.AddDependency("Material", "Texture");

        graph.GetOrUpdate("File", _ => 1);
        graph.GetOrUpdate("Texture", _ => 2);
        graph.GetOrUpdate("Material", _ => 3);

        graph.Invalidate("File");

        var updated = new List<string>();
        graph.UpdateDirty(key =>
        {
            updated.Add(key);
            return 42;
        });

        Assert.Contains("Texture", updated);
        Assert.Contains("Material", updated);
    }

    [Fact]
    public void GetOrUpdate_UsesCache()
    {
        var graph = new DependencyGraph<int, int>();
        int calls = 0;

        int Compute(int key)
        {
            calls++;
            return key * 2;
        }

        var a = graph.GetOrUpdate(1, Compute);
        var b = graph.GetOrUpdate(1, Compute);

        Assert.Equal(2, a);
        Assert.Equal(2, b);
        Assert.Equal(1, calls);
    }

    [Fact]
    public void UpdateDirty_RespectsMaxCount()
    {
        var graph = new DependencyGraph<int, int>();
        graph.AddNode(1);
        graph.AddNode(2);
        graph.AddNode(3);

        graph.GetOrUpdate(1, _ => 1);
        graph.GetOrUpdate(2, _ => 2);
        graph.GetOrUpdate(3, _ => 3);

        graph.Invalidate(1);
        graph.Invalidate(2);
        graph.Invalidate(3);

        var updated = graph.UpdateDirty(_ => 5, maxCount: 2);
        Assert.Equal(2, updated);
    }

    [Fact]
    public void DependencyCacheMode_Disabled_BypassesCache()
    {
        var graph = new DependencyGraph<int, int> { dependencyCacheMode = DependencyCacheMode.Disabled };
        int calls = 0;

        int Compute(int key)
        {
            calls++;
            return key;
        }

        graph.GetOrUpdate(1, Compute);
        graph.GetOrUpdate(1, Compute);
        Assert.Equal(2, calls);
    }
}
