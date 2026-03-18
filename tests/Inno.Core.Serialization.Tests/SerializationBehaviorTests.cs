using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Xunit;

namespace Inno.Core.Serialization.Tests;

public sealed class SerializationBehaviorTests
{
    [Fact]
    public void SerializablePropertyAttribute_DefaultAndCustomVisibility()
    {
        var @default = new SerializablePropertyAttribute();
        var custom = new SerializablePropertyAttribute(PropertyVisibility.Readonly);

        Assert.Equal(PropertyVisibility.Show, @default.propertyVisibility);
        Assert.Equal(PropertyVisibility.Readonly, custom.propertyVisibility);
    }

    [Fact]
    public void SerializingState_QueryApis_WorkAsExpected()
    {
        var state = new SerializingState(new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["name"] = "Player",
            ["score"] = 42
        });

        Assert.True(state.Contains("name"));
        Assert.True(state.TryGetValue("score", out var scoreRaw));
        Assert.Equal(42, scoreRaw);
        Assert.Equal("Player", state.GetValue<string>("name"));
        Assert.Throws<KeyNotFoundException>(() => state.GetValue<string>("missing"));
        Assert.Throws<InvalidCastException>(() => state.GetValue<int>("name"));
    }

    [Fact]
    public void SerializingState_Serialize_IsDeterministicForStringKeyMaps()
    {
        var s1 = new SerializingState(new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["b"] = 2,
            ["a"] = 1
        });
        var s2 = new SerializingState(new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["a"] = 1,
            ["b"] = 2
        });

        var b1 = SerializingState.Serialize(s1);
        var b2 = SerializingState.Serialize(s2);

        Assert.Equal(b1, b2);
    }

    [Fact]
    public void SerializingState_Deserialize_InvalidHeader_Throws()
    {
        using var ms = new MemoryStream();
        using (var bw = new BinaryWriter(ms))
        {
            bw.Write("BAD");
            bw.Write(1);
            bw.Write((byte)20); // State kind
            bw.Write(0);
        }

        Assert.Throws<InvalidDataException>(() => SerializingState.Deserialize(ms.ToArray()));
    }

    [Fact]
    public void SerializingState_SerializeDeserialize_SupportsRuntimeMapAndSequenceTypes()
    {
        var map = new CustomMap();
        map.Add("x", 10);
        map.Add("y", 20);

        var queue = new ConcurrentQueue<int>(new[] { 4, 5, 6 });

        var state = new SerializingState(new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["map"] = map,
            ["queue"] = queue
        });

        var bytes = SerializingState.Serialize(state);
        var restored = SerializingState.Deserialize(bytes);

        var rawMap = Assert.IsType<Dictionary<object, object?>>(restored.GetValue<object>("map"));
        Assert.Equal(10L, Convert.ToInt64(rawMap["x"]));
        Assert.Equal(20L, Convert.ToInt64(rawMap["y"]));

        var rawQueue = Assert.IsType<List<object?>>(restored.GetValue<object>("queue"));
        Assert.Equal(new object?[] { 4, 5, 6 }, rawQueue);
    }

    [Fact]
    public void ISerializable_CaptureRestore_AndBinaryRoundtrip_WorkForPublicApi()
    {
        var source = new SampleSerializable
        {
            id = 7,
            readOnlyValue = "ReadOnly",
            hiddenValue = "Hidden",
            serializeOnly = 88,
            deserializeOnly = 99,
            mode = SampleMode.Hard,
            guid = Guid.Parse("0f7a06c8-d2ef-4c24-a5d1-34b4f12d0124"),
            stats = new SampleStats { hp = 33, mp = 17 },
            child = new SampleChild { level = 5 },
            numbers = new List<int> { 1, 2, 3 },
            map = new Dictionary<string, int>(StringComparer.Ordinal)
            {
                ["z"] = 10,
                ["a"] = 20
            },
            hashSet = new HashSet<int> { 8, 9 },
            concurrentQueue = new ConcurrentQueue<int>(new[] { 11, 12 }),
            concurrentMap = new ConcurrentDictionary<string, int>(new[]
            {
                new KeyValuePair<string, int>("one", 1),
                new KeyValuePair<string, int>("two", 2)
            }),
            customSequence = new CustomSequence { 13, 14 },
            customMap = new CustomMap
            {
                ["alpha"] = 100,
                ["beta"] = 200
            }
        };

        ISerializable serializableSource = source;
        var state = serializableSource.CaptureState();
        var bytes = SerializingState.Serialize(state);
        var restoredState = SerializingState.Deserialize(bytes);

        var target = new SampleSerializable();
        ISerializable serializableTarget = target;
        serializableTarget.RestoreState(restoredState);

        Assert.Equal(7, target.id);
        Assert.Equal("ReadOnly", target.readOnlyValue);
        Assert.Equal("Hidden", target.hiddenValue);
        Assert.Equal(0, target.serializeOnly); // SerializeOnly should not deserialize.
        Assert.Equal(0, target.deserializeOnly);
        Assert.Equal(SampleMode.Hard, target.mode);
        Assert.Equal(Guid.Parse("0f7a06c8-d2ef-4c24-a5d1-34b4f12d0124"), target.guid);
        Assert.Equal(33, target.stats.hp);
        Assert.Equal(17, target.stats.mp);
        Assert.NotNull(target.child);
        Assert.Equal(5, target.child.level);
        Assert.Equal(new[] { 1, 2, 3 }, target.numbers);
        Assert.Equal(20, target.map["a"]);
        Assert.Equal(10, target.map["z"]);
        Assert.True(target.hashSet.SetEquals(new[] { 8, 9 }));
        Assert.Equal(new[] { 11, 12 }, target.concurrentQueue.ToArray());
        Assert.Equal(1, target.concurrentMap["one"]);
        Assert.Equal(2, target.concurrentMap["two"]);
        Assert.Equal(new[] { 13, 14 }, target.customSequence.ToArray());
        Assert.Equal(100, target.customMap["alpha"]);
        Assert.Equal(200, target.customMap["beta"]);
    }

    [Fact]
    public void ISerializable_GetSerializedProperties_ExposesRuntimeApiAndHonorsVisibility()
    {
        var instance = new SampleSerializable
        {
            id = 1,
            readOnlyValue = "locked",
            hiddenValue = "hidden"
        };

        ISerializable serializable = instance;
        var props = serializable.GetSerializedProperties();
        Assert.DoesNotContain(props, p => p.name == nameof(SampleSerializable.hiddenValue));

        var idProp = props.Single(p => p.name == nameof(SampleSerializable.id));
        Assert.Equal(1, idProp.GetValue());
        idProp.SetValue(5);
        Assert.Equal(5, instance.id);

        var readOnlyProp = props.Single(p => p.name == nameof(SampleSerializable.readOnlyValue));
        Assert.Equal("locked", readOnlyProp.GetValue());
        readOnlyProp.SetValue("new");
        Assert.Equal("locked", instance.readOnlyValue);
    }

    [Fact]
    public void ISerializable_RestoreState_AppliesDeserializeOnlyMembers()
    {
        var state = new SerializingState(new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            [nameof(SampleSerializable.deserializeOnly)] = 123
        });

        var instance = new SampleSerializable();
        ISerializable serializable = instance;
        serializable.RestoreState(state);

        Assert.Equal(123, instance.deserializeOnly);
    }

    [Fact]
    public void ISerializable_RestoreState_InvokesHooks_BaseBeforeDerived()
    {
        ISerializable source = new HookChildSerializable { value = 3 };
        var state = source.CaptureState();
        var restored = new HookChildSerializable();
        ISerializable serializable = restored;

        serializable.RestoreState(state);

        Assert.Equal(new[] { "base", "child" }, restored.hookCalls);
    }

    [Fact]
    public void ISerializable_CreateSerializableInstance_SupportsPrivateCtorAndFallback()
    {
        var privateCtor = ISerializable.CreateSerializableInstance(typeof(PrivateCtorSerializable));
        Assert.IsType<PrivateCtorSerializable>(privateCtor);
        Assert.True(((PrivateCtorSerializable)privateCtor).constructed);

        var fallbackCtor = ISerializable.CreateSerializableInstance(typeof(ThrowingCtorSerializable));
        Assert.IsType<ThrowingCtorSerializable>(fallbackCtor);
        Assert.False(((ThrowingCtorSerializable)fallbackCtor).ctorCompleted);
    }

    [Fact]
    public void ISerializable_CreateSerializableInstance_InvalidType_Throws()
    {
        Assert.Throws<InvalidCastException>(() => ISerializable.CreateSerializableInstance(typeof(string)));
    }
}

public enum SampleMode
{
    Easy = 0,
    Hard = 1
}

public struct SampleStats
{
    [SerializableProperty]
    public int hp;

    [SerializableProperty]
    public int mp { get; set; }
}

public sealed class SampleChild : ISerializable
{
    [SerializableProperty]
    public int level { get; set; }
}

public class SampleSerializable : ISerializable
{
    [SerializableProperty]
    public int id { get; set; }

    [SerializableProperty(PropertyVisibility.Hide)]
    public string hiddenValue { get; set; } = string.Empty;

    [SerializableProperty(PropertyVisibility.Readonly)]
    public string readOnlyValue { get; set; } = string.Empty;

    [SerializableProperty(PropertyVisibility.SerializeOnly)]
    public int serializeOnly { get; set; }

    [SerializableProperty(PropertyVisibility.DeserializeOnly)]
    public int deserializeOnly { get; set; }

    [SerializableProperty]
    public SampleMode mode { get; set; }

    [SerializableProperty]
    public Guid guid { get; set; }

    [SerializableProperty]
    public SampleStats stats;

    [SerializableProperty]
    public SampleChild child { get; set; } = new();

    [SerializableProperty]
    public List<int> numbers { get; set; } = new();

    [SerializableProperty]
    public Dictionary<string, int> map { get; set; } = new(StringComparer.Ordinal);

    [SerializableProperty]
    public HashSet<int> hashSet { get; set; } = [];

    [SerializableProperty]
    public ConcurrentQueue<int> concurrentQueue { get; set; } = new();

    [SerializableProperty]
    public ConcurrentDictionary<string, int> concurrentMap { get; set; } = new();

    [SerializableProperty]
    public CustomSequence customSequence { get; set; } = new();

    [SerializableProperty]
    public CustomMap customMap { get; set; } = new();
}

public sealed class CustomSequence : IEnumerable<int>
{
    private readonly List<int> m_values = new();

    public void Add(int value) => m_values.Add(value);

    public IEnumerator<int> GetEnumerator() => m_values.GetEnumerator();

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
}

public sealed class CustomMap : IEnumerable<KeyValuePair<string, int>>
{
    private readonly Dictionary<string, int> m_values = new(StringComparer.Ordinal);

    public void Add(string key, int value) => m_values.Add(key, value);

    public int this[string key]
    {
        get => m_values[key];
        set => m_values[key] = value;
    }

    public IEnumerator<KeyValuePair<string, int>> GetEnumerator() => m_values.GetEnumerator();

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
}

public class HookBaseSerializable : ISerializable
{
    [SerializableProperty]
    public int value { get; set; }

    public List<string> hookCalls { get; } = new();

    [OnSerializableRestored]
    private void OnBaseRestored()
    {
        hookCalls.Add("base");
    }
}

public sealed class HookChildSerializable : HookBaseSerializable
{
    [OnSerializableRestored]
    private void OnChildRestored()
    {
        hookCalls.Add("child");
    }
}

public sealed class PrivateCtorSerializable : ISerializable
{
    public bool constructed { get; }

    private PrivateCtorSerializable()
    {
        constructed = true;
    }
}

public sealed class ThrowingCtorSerializable : ISerializable
{
    public bool ctorCompleted { get; }

    private ThrowingCtorSerializable()
    {
        ctorCompleted = true;
        throw new InvalidOperationException("boom");
    }
}
