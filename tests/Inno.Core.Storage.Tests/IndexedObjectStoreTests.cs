using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

using Inno.Core.Storage;

using Xunit;

namespace Inno.Core.Storage.Tests;

public sealed class IndexedObjectStoreTests
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
        var store = new IndexedObjectStore<Item>();
        var a = new Item("A", 1);
        var b = new Item("B", 2);

        store.Add(a);
        store.Add(b);
        Assert.Equal(2, store.count);

        Assert.True(store.Remove(a));
        Assert.Equal(1, store.count);
        Assert.False(store.Remove(a));

        store.Clear();
        Assert.Equal(0, store.count);
    }

    [Fact]
    public void DefineKey_UniqueAndLookup()
    {
        var store = new IndexedObjectStore<Item>();
        var key = store.DefineKey<int>("id", IndexedObjectKeyFlags.Unique);

        var a = new Item("A", 1);
        var b = new Item("B", 2);

        store.Add(a).Set(key, 10);
        store.Add(b).Set(key, 20);

        var found = store.First(key, 20);
        Assert.Same(b, found);

        var all = store.Find(key, 10).ToList();
        Assert.Single(all);
        Assert.Same(a, all[0]);
    }

    [Fact]
    public void DefineKey_Unique_ThrowsOnDuplicateValueFromDifferentItems()
    {
        var store = new IndexedObjectStore<Item>();
        var key = store.DefineKey<int>("id", IndexedObjectKeyFlags.Unique);

        var a = new Item("A", 1);
        var b = new Item("B", 2);

        store.Add(a).Set(key, 10);

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
            store.Add(b).Set(key, 10));

        Assert.Contains("Duplicate key", ex.Message);
    }

    [Fact]
    public void DefineKey_Unique_AllowsResettingSameValueOnSameItem()
    {
        var store = new IndexedObjectStore<Item>();
        var key = store.DefineKey<int>("id", IndexedObjectKeyFlags.Unique);

        var a = new Item("A", 1);
        
        store.Add(a).Set(key, 10);
        store.Add(a).Set(key, 20);

        Item? found = store.First(key, 20);
        Assert.Same(a, found);
    }

    [Fact]
    public void DefineKey_MultiAndQuery()
    {
        var store = new IndexedObjectStore<Item>();
        var key = store.DefineKey<int>("category", IndexedObjectKeyFlags.Unordered);

        var a = new Item("A", 1);
        var b = new Item("B", 1);
        var c = new Item("C", 2);

        store.Add(a).Set(key, 1);
        store.Add(b).Set(key, 1);
        store.Add(c).Set(key, 2);

        var results = store.Find(key, 1).ToList();
        Assert.Equal(2, results.Count);
        Assert.Contains(a, results);
        Assert.Contains(b, results);

        var query = store.Query().Find(key, 2);
        var single = query.First();
        Assert.Same(c, single);
    }

    [Fact]
    public void OrderBy_UsesSortedKey()
    {
        var store = new IndexedObjectStore<Item>();
        var key = store.DefineKey<int>("ordered", IndexedObjectKeyFlags.Ordered, Comparer<int>.Default);

        var a = new Item("A", 1);
        var b = new Item("B", 1);
        var c = new Item("C", 1);

        store.Add(a).Set(key, 3);
        store.Add(b).Set(key, 1);
        store.Add(c).Set(key, 2);

        var ordered = store.Query().OrderBy(key).Get().ToList();
        Assert.Equal(3, ordered.Count);
        Assert.Same(b, ordered[0]);
        Assert.Same(c, ordered[1]);
        Assert.Same(a, ordered[2]);
    }

    [Fact]
    public void RemoveKey_InvalidatesKey()
    {
        var store = new IndexedObjectStore<Item>();
        var key = store.DefineKey<int>("temp");

        var item = new Item("A", 1);
        store.Add(item).Set(key, 1);

        Assert.True(key.isValid);
        Assert.True(store.RemoveKey(key));
        Assert.False(key.isValid);
    }

    [Fact]
    public void TryGetKey_ResolvesByName()
    {
        var store = new IndexedObjectStore<Item>();
        var key = store.DefineKey<int>("resolve");

        Assert.True(store.TryGetKey("resolve", out IndexedObjectKey<int> resolved));
        Assert.True(resolved.isValid);
        Assert.Equal(key.name, resolved.name);
    }

    [Fact]
    public void PredicateQuery_Works()
    {
        var store = new IndexedObjectStore<Item>();
        var a = new Item("A", 1);
        var b = new Item("B", 2);

        store.Add(a);
        store.Add(b);

        var results = store.Query()
            .Where(item => item.Category == 2)
            .Get()
            .ToList();

        Assert.Single(results);
        Assert.Same(b, results[0]);
    }

    [Fact]
    public void GetFast_IsFailFast_WhenStoreMutates()
    {
        var store = new IndexedObjectStore<Item>();
        store.Add(new Item("A", 1));
        store.Add(new Item("B", 2));

        using var enumerator = store.Query().GetFast().GetEnumerator();
        Assert.True(enumerator.MoveNext());

        store.Add(new Item("C", 3));

        Assert.Throws<InvalidOperationException>(() => enumerator.MoveNext());
    }

    [Fact]
    public void Get_ReturnsStableSnapshot_WhenStoreMutates()
    {
        var store = new IndexedObjectStore<Item>();
        var a = new Item("A", 1);
        var b = new Item("B", 2);
        store.Add(a);
        store.Add(b);

        IReadOnlyList<Item> snapshot = store.Query().Get();
        store.Add(new Item("C", 3));

        Assert.Equal(2, snapshot.Count);
        Assert.Contains(a, snapshot);
        Assert.Contains(b, snapshot);
    }

    [Fact]
    public void RemoveAll_KeepsKeys()
    {
        var store = new IndexedObjectStore<Item>();
        var key = store.DefineKey<int>("category");

        var a = new Item("A", 1);
        store.Add(a).Set(key, 1);

        store.RemoveAll();
        Assert.Equal(0, store.count);
        Assert.True(key.isValid);
    }

    [Fact]
    public void All_ReturnsStableSnapshot_WhenStoreMutates()
    {
        var store = new IndexedObjectStore<Item>();
        var a = new Item("A", 1);
        var b = new Item("B", 2);
        store.Add(a);
        store.Add(b);

        IReadOnlyList<Item> snapshot = store.All();
        store.Add(new Item("C", 3));

        Assert.Equal(2, snapshot.Count);
        Assert.Contains(a, snapshot);
        Assert.Contains(b, snapshot);
    }

    [Fact]
    public void AllFast_ReturnsAllItems()
    {
        var store = new IndexedObjectStore<Item>();
        var a = new Item("A", 1);
        var b = new Item("B", 2);
        var c = new Item("C", 3);
        store.Add(a);
        store.Add(b);
        store.Add(c);

        IReadOnlyList<Item> all = store.AllFast().ToList();

        Assert.Equal(3, all.Count);
        Assert.Contains(a, all);
        Assert.Contains(b, all);
        Assert.Contains(c, all);
    }

    [Fact]
    public void AllFast_IsFailFast_WhenStoreMutates()
    {
        var store = new IndexedObjectStore<Item>();
        store.Add(new Item("A", 1));
        store.Add(new Item("B", 2));

        using var enumerator = store.AllFast().GetEnumerator();
        Assert.True(enumerator.MoveNext());

        store.Add(new Item("C", 3));

        Assert.Throws<InvalidOperationException>(() => enumerator.MoveNext());
    }

    [Fact]
    public void RuntimeHandle_ResolvesItem_WhenAlive()
    {
        var store = new IndexedObjectStore<Item>();
        var a = new Item("A", 1);
        store.Add(a);

        object handle = GetHandle(store, a);
        Assert.True(IsHandleValid(store, handle));
        Assert.True(TryGetByHandle(store, handle, out Item? resolved));
        Assert.Same(a, resolved);
    }

    [Fact]
    public void RuntimeHandle_BecomesInvalid_AfterRemove()
    {
        var store = new IndexedObjectStore<Item>();
        var a = new Item("A", 1);
        store.Add(a);
        object handle = GetHandle(store, a);

        Assert.True(store.Remove(a));
        Assert.False(IsHandleValid(store, handle));
        Assert.False(TryGetByHandle(store, handle, out _));
    }

    [Fact]
    public void RuntimeHandle_InvalidatesOldGeneration_WhenSlotReused()
    {
        var store = new IndexedObjectStore<Item>();
        var first = new Item("First", 1);
        store.Add(first);
        object firstHandle = GetHandle(store, first);

        Assert.True(store.Remove(first));

        var second = new Item("Second", 2);
        store.Add(second);
        object secondHandle = GetHandle(store, second);

        Assert.Equal(ReadHandleField<int>(firstHandle, "slot"), ReadHandleField<int>(secondHandle, "slot"));
        Assert.NotEqual(ReadHandleField<uint>(firstHandle, "generation"), ReadHandleField<uint>(secondHandle, "generation"));
        Assert.False(IsHandleValid(store, firstHandle));
        Assert.True(IsHandleValid(store, secondHandle));
    }

    [Fact]
    public void RuntimeHandle_InvalidatesAfterRemoveAll()
    {
        var store = new IndexedObjectStore<Item>();
        var key = store.DefineKey<int>("category");
        var a = new Item("A", 1);

        store.Add(a).Set(key, a.Category);
        object handle = GetHandle(store, a);

        store.RemoveAll();

        Assert.True(key.isValid);
        Assert.False(IsHandleValid(store, handle));
        Assert.False(TryGetByHandle(store, handle, out _));
    }

    private static object GetHandle(IndexedObjectStore<Item> store, Item item)
    {
        MethodInfo tryGetHandle = store.GetType().GetMethod("TryGetHandle", BindingFlags.Instance | BindingFlags.NonPublic)!;
        object?[] args = [item, null];
        bool ok = (bool)tryGetHandle.Invoke(store, args)!;
        Assert.True(ok);
        return args[1]!;
    }

    private static bool IsHandleValid(IndexedObjectStore<Item> store, object handle)
    {
        MethodInfo isHandleValid = store.GetType().GetMethod("IsHandleValid", BindingFlags.Instance | BindingFlags.NonPublic)!;
        return (bool)isHandleValid.Invoke(store, [handle])!;
    }

    private static bool TryGetByHandle(IndexedObjectStore<Item> store, object handle, out Item? resolved)
    {
        MethodInfo tryGetByHandle = store.GetType().GetMethod("TryGetByHandle", BindingFlags.Instance | BindingFlags.NonPublic)!;
        object?[] args = [handle, null];
        bool ok = (bool)tryGetByHandle.Invoke(store, args)!;
        resolved = (Item?)args[1];
        return ok;
    }

    private static TField ReadHandleField<TField>(object handle, string fieldName)
    {
        PropertyInfo property = handle.GetType().GetProperty(fieldName, BindingFlags.Instance | BindingFlags.Public)!;
        return (TField)property.GetValue(handle)!;
    }
}
