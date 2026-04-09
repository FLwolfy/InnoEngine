using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

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
    public void DefineKey_Unique_ThrowsOnDuplicateValueFromDifferentItems()
    {
        var pool = new ObjectPool<Item>();
        var key = pool.DefineKey<int>("id", PoolKeyFlags.Unique);

        var a = new Item("A", 1);
        var b = new Item("B", 2);

        pool.Add(a).Set(key, 10);

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
            pool.Add(b).Set(key, 10));

        Assert.Contains("Duplicate key", ex.Message);
    }

    [Fact]
    public void DefineKey_Unique_AllowsResettingSameValueOnSameItem()
    {
        var pool = new ObjectPool<Item>();
        var key = pool.DefineKey<int>("id", PoolKeyFlags.Unique);

        var a = new Item("A", 1);
        
        pool.Add(a).Set(key, 10);
        pool.Add(a).Set(key, 20);

        Item? found = pool.First(key, 20);
        Assert.Same(a, found);
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
    public void GetFast_IsFailFast_WhenPoolMutates()
    {
        var pool = new ObjectPool<Item>();
        pool.Add(new Item("A", 1));
        pool.Add(new Item("B", 2));

        using var enumerator = pool.Query().GetFast().GetEnumerator();
        Assert.True(enumerator.MoveNext());

        pool.Add(new Item("C", 3));

        Assert.Throws<InvalidOperationException>(() => enumerator.MoveNext());
    }

    [Fact]
    public void Get_ReturnsStableSnapshot_WhenPoolMutates()
    {
        var pool = new ObjectPool<Item>();
        var a = new Item("A", 1);
        var b = new Item("B", 2);
        pool.Add(a);
        pool.Add(b);

        IReadOnlyList<Item> snapshot = pool.Query().Get();
        pool.Add(new Item("C", 3));

        Assert.Equal(2, snapshot.Count);
        Assert.Contains(a, snapshot);
        Assert.Contains(b, snapshot);
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

    [Fact]
    public void All_ReturnsStableSnapshot_WhenPoolMutates()
    {
        var pool = new ObjectPool<Item>();
        var a = new Item("A", 1);
        var b = new Item("B", 2);
        pool.Add(a);
        pool.Add(b);

        IReadOnlyList<Item> snapshot = pool.All();
        pool.Add(new Item("C", 3));

        Assert.Equal(2, snapshot.Count);
        Assert.Contains(a, snapshot);
        Assert.Contains(b, snapshot);
    }

    [Fact]
    public void AllFast_ReturnsAllItems()
    {
        var pool = new ObjectPool<Item>();
        var a = new Item("A", 1);
        var b = new Item("B", 2);
        var c = new Item("C", 3);
        pool.Add(a);
        pool.Add(b);
        pool.Add(c);

        IReadOnlyList<Item> all = pool.AllFast().ToList();

        Assert.Equal(3, all.Count);
        Assert.Contains(a, all);
        Assert.Contains(b, all);
        Assert.Contains(c, all);
    }

    [Fact]
    public void AllFast_IsFailFast_WhenPoolMutates()
    {
        var pool = new ObjectPool<Item>();
        pool.Add(new Item("A", 1));
        pool.Add(new Item("B", 2));

        using var enumerator = pool.AllFast().GetEnumerator();
        Assert.True(enumerator.MoveNext());

        pool.Add(new Item("C", 3));

        Assert.Throws<InvalidOperationException>(() => enumerator.MoveNext());
    }

    [Fact]
    public void RuntimeHandle_ResolvesItem_WhenAlive()
    {
        var pool = new ObjectPool<Item>();
        var a = new Item("A", 1);
        pool.Add(a);

        object handle = GetHandle(pool, a);
        Assert.True(IsHandleValid(pool, handle));
        Assert.True(TryGetByHandle(pool, handle, out Item? resolved));
        Assert.Same(a, resolved);
    }

    [Fact]
    public void RuntimeHandle_BecomesInvalid_AfterRemove()
    {
        var pool = new ObjectPool<Item>();
        var a = new Item("A", 1);
        pool.Add(a);
        object handle = GetHandle(pool, a);

        Assert.True(pool.Remove(a));
        Assert.False(IsHandleValid(pool, handle));
        Assert.False(TryGetByHandle(pool, handle, out _));
    }

    [Fact]
    public void RuntimeHandle_InvalidatesOldGeneration_WhenSlotReused()
    {
        var pool = new ObjectPool<Item>();
        var first = new Item("First", 1);
        pool.Add(first);
        object firstHandle = GetHandle(pool, first);

        Assert.True(pool.Remove(first));

        var second = new Item("Second", 2);
        pool.Add(second);
        object secondHandle = GetHandle(pool, second);

        Assert.Equal(ReadHandleField<int>(firstHandle, "slot"), ReadHandleField<int>(secondHandle, "slot"));
        Assert.NotEqual(ReadHandleField<uint>(firstHandle, "generation"), ReadHandleField<uint>(secondHandle, "generation"));
        Assert.False(IsHandleValid(pool, firstHandle));
        Assert.True(IsHandleValid(pool, secondHandle));
    }

    [Fact]
    public void RuntimeHandle_InvalidatesAfterRemoveAll()
    {
        var pool = new ObjectPool<Item>();
        var key = pool.DefineKey<int>("category");
        var a = new Item("A", 1);

        pool.Add(a).Set(key, a.Category);
        object handle = GetHandle(pool, a);

        pool.RemoveAll();

        Assert.True(key.isValid);
        Assert.False(IsHandleValid(pool, handle));
        Assert.False(TryGetByHandle(pool, handle, out _));
    }

    private static object GetHandle(ObjectPool<Item> pool, Item item)
    {
        MethodInfo tryGetHandle = pool.GetType().GetMethod("TryGetHandle", BindingFlags.Instance | BindingFlags.NonPublic)!;
        object?[] args = [item, null];
        bool ok = (bool)tryGetHandle.Invoke(pool, args)!;
        Assert.True(ok);
        return args[1]!;
    }

    private static bool IsHandleValid(ObjectPool<Item> pool, object handle)
    {
        MethodInfo isHandleValid = pool.GetType().GetMethod("IsHandleValid", BindingFlags.Instance | BindingFlags.NonPublic)!;
        return (bool)isHandleValid.Invoke(pool, [handle])!;
    }

    private static bool TryGetByHandle(ObjectPool<Item> pool, object handle, out Item? resolved)
    {
        MethodInfo tryGetByHandle = pool.GetType().GetMethod("TryGetByHandle", BindingFlags.Instance | BindingFlags.NonPublic)!;
        object?[] args = [handle, null];
        bool ok = (bool)tryGetByHandle.Invoke(pool, args)!;
        resolved = (Item?)args[1];
        return ok;
    }

    private static TField ReadHandleField<TField>(object handle, string fieldName)
    {
        PropertyInfo property = handle.GetType().GetProperty(fieldName, BindingFlags.Instance | BindingFlags.Public)!;
        return (TField)property.GetValue(handle)!;
    }
}
