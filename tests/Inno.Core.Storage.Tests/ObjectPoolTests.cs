using System;
using System.Collections.Generic;
using System.Linq;

using Inno.Core.Storage;

using Xunit;

namespace Inno.Core.Storage.Tests;

public sealed class ObjectPoolTests
{
    private sealed class Item
    {
        public string name { get; }
        public int Category { get; }

        public Item(string name, int category)
        {
            this.name = name;
            Category = category;
        }
    }

    [Fact]
    public void Add_Remove_Count_Clear()
    {
        var pool = new ObjectPool<Item>();
        var a = new Item("A", 1);
        var b = new Item("B", 2);

        pool.Add(a);
        pool.Add(b);
        Assert.Equal(2, pool.count);

        Assert.True(pool.Remove(a));
        Assert.Equal(1, pool.count);
        Assert.False(pool.Remove(a));

        pool.Clear();
        Assert.Equal(0, pool.count);
    }

    [Fact]
    public void DefineKey_UniqueAndLookup()
    {
        var pool = new ObjectPool<Item>();
        var key = pool.DefineKey<int>("id", PoolKeyFlags.Unique);

        var a = new Item("A", 1);
        var b = new Item("B", 2);

        pool.Add(a).Set(key, 10);
        pool.Add(b).Set(key, 20);

        var found = pool.First(key, 20);
        Assert.Same(b, found);

        var all = pool.Find(key, 10).ToList();
        Assert.Single(all);
        Assert.Same(a, all[0]);
    }

    [Fact]
    public void DefineKey_MultiAndQuery()
    {
        var pool = new ObjectPool<Item>();
        var key = pool.DefineKey<int>("category", PoolKeyFlags.Unordered);

        var a = new Item("A", 1);
        var b = new Item("B", 1);
        var c = new Item("C", 2);

        pool.Add(a).Set(key, 1);
        pool.Add(b).Set(key, 1);
        pool.Add(c).Set(key, 2);

        var results = pool.Find(key, 1).ToList();
        Assert.Equal(2, results.Count);
        Assert.Contains(a, results);
        Assert.Contains(b, results);

        var query = pool.Query().Find(key, 2);
        var single = query.First();
        Assert.Same(c, single);
    }

    [Fact]
    public void OrderBy_UsesSortedKey()
    {
        var pool = new ObjectPool<Item>();
        var key = pool.DefineKey<int>("ordered", PoolKeyFlags.Ordered, Comparer<int>.Default);

        var a = new Item("A", 1);
        var b = new Item("B", 1);
        var c = new Item("C", 1);

        pool.Add(a).Set(key, 3);
        pool.Add(b).Set(key, 1);
        pool.Add(c).Set(key, 2);

        var ordered = pool.Query().OrderBy(key).Get().ToList();
        Assert.Equal(3, ordered.Count);
        Assert.Same(b, ordered[0]);
        Assert.Same(c, ordered[1]);
        Assert.Same(a, ordered[2]);
    }

    [Fact]
    public void RemoveKey_InvalidatesKey()
    {
        var pool = new ObjectPool<Item>();
        var key = pool.DefineKey<int>("temp");

        var item = new Item("A", 1);
        pool.Add(item).Set(key, 1);

        Assert.True(key.isValid);
        Assert.True(pool.RemoveKey(key));
        Assert.False(key.isValid);
    }

    [Fact]
    public void TryGetKey_ResolvesByName()
    {
        var pool = new ObjectPool<Item>();
        var key = pool.DefineKey<int>("resolve");

        Assert.True(pool.TryGetKey("resolve", out PoolKey<int> resolved));
        Assert.True(resolved.isValid);
        Assert.Equal(key.name, resolved.name);
    }

    [Fact]
    public void QueryPredicate_Works()
    {
        var pool = new ObjectPool<Item>();
        var a = new Item("A", 1);
        var b = new Item("B", 2);

        pool.Add(a);
        pool.Add(b);

        var results = pool.Query()
            .Where(item => item.Category == 2)
            .Get()
            .ToList();

        Assert.Single(results);
        Assert.Same(b, results[0]);
    }

    [Fact]
    public void RemoveAll_KeepsKeys()
    {
        var pool = new ObjectPool<Item>();
        var key = pool.DefineKey<int>("category");

        var a = new Item("A", 1);
        pool.Add(a).Set(key, 1);

        pool.RemoveAll();
        Assert.Equal(0, pool.count);
        Assert.True(key.isValid);
    }
}
