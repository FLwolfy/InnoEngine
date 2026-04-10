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
    public void SerializingState_Deserialize_LegacyTypeToken_Throws()
    {
        using var ms = new MemoryStream();
        using (var bw = new BinaryWriter(ms))
        {
            bw.Write("INNO");
            bw.Write(1);
            bw.Write((byte)20); // BinKind.State
            bw.Write(1); // key count
            bw.Write("value");
            bw.Write((byte)2); // BinKind.Enum
            bw.Write(typeof(SampleMode).AssemblyQualifiedName!); // legacy token without stable:/runtime:
            bw.Write((long)SampleMode.Hard);
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

    [Fact]
    public void SerializableManager_AutoDiscoversCodec_ForCustomType()
    {
        var source = new CodecDrivenSerializable
        {
            blob = new ExternalBlob { value = 41 }
        };

        ISerializable serializable = source;
        SerializingState state = serializable.CaptureState();

        var target = new CodecDrivenSerializable();
        ((ISerializable)target).RestoreState(state);
        Assert.Equal(41, target.blob.value);

        byte[] bytes = SerializingState.Serialize(state);
        SerializingState restoredState = SerializingState.Deserialize(bytes);
        var fromBinary = new CodecDrivenSerializable();
        ((ISerializable)fromBinary).RestoreState(restoredState);
        Assert.Equal(41, fromBinary.blob.value);
    }

    [Fact]
    public void SerializableManager_HasCodec_CoversPrimitiveClassAndStructSplitRoutes()
    {
        Assert.True(SerializableManager.HasCodec(typeof(int)));
        Assert.True(SerializableManager.HasCodec(typeof(SampleSerializable)));
        Assert.True(SerializableManager.HasCodec(typeof(SampleStats)));
        Assert.True(SerializableManager.HasCodec(typeof(ExternalBlob)));
    }

    [Fact]
    public void StructSplitRoute_WorksForStructWithoutISerializable()
    {
        var source = new StructHolderSerializable
        {
            payload = new PlainPayloadStruct
            {
                hp = 77,
                mp = 55
            }
        };

        SerializingState state = ((ISerializable)source).CaptureState();
        var restored = new StructHolderSerializable();
        ((ISerializable)restored).RestoreState(state);

        Assert.Equal(77, restored.payload.hp);
        Assert.Equal(55, restored.payload.mp);
    }

    [Fact]
    public void CaptureState_WhenNoCodecAndNoSplitRoute_Throws()
    {
        var source = new NoCodecHolderSerializable
        {
            value = new NoCodecType { number = 12 }
        };

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => ((ISerializable)source).CaptureState());
        Assert.Contains("No serialization codec found", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CodecInheritanceFallback_UsesBaseCodecWhenDerivedCodecMissing()
    {
        var source = new InheritanceHolderSerializable
        {
            animal = new DerivedAnimal { name = "fox", level = 3 }
        };

        SerializingState state = ((ISerializable)source).CaptureState();

        var restored = new InheritanceHolderSerializable();
        ((ISerializable)restored).RestoreState(state);

        Assert.IsType<DerivedAnimal>(restored.animal);
        Assert.Equal("fox", restored.animal.name);
        Assert.Equal(3, restored.animal.level);
    }

    [Fact]
    public void CollectionCodec_UsesElementCodecThroughContextRecursion()
    {
        var source = new BlobListHolderSerializable
        {
            values =
            [
                new ExternalBlob { value = 1 },
                new ExternalBlob { value = 2 },
                new ExternalBlob { value = 3 }
            ]
        };

        SerializingState state = ((ISerializable)source).CaptureState();
        var restored = new BlobListHolderSerializable();
        ((ISerializable)restored).RestoreState(state);

        Assert.Equal(new[] { 1, 2, 3 }, restored.values.Select(static b => b.value).ToArray());
    }

    [Fact]
    public void MapCodec_UsesValueCodecThroughContextRecursion()
    {
        var source = new BlobMapHolderSerializable
        {
            values = new Dictionary<string, ExternalBlob>(StringComparer.Ordinal)
            {
                ["a"] = new ExternalBlob { value = 10 },
                ["b"] = new ExternalBlob { value = 20 }
            }
        };

        SerializingState state = ((ISerializable)source).CaptureState();
        var restored = new BlobMapHolderSerializable();
        ((ISerializable)restored).RestoreState(state);

        Assert.Equal(10, restored.values["a"].value);
        Assert.Equal(20, restored.values["b"].value);
    }

    [Fact]
    public void CaptureState_WhenCycleReferenceExists_Throws()
    {
        var a = new CycleNodeSerializable { name = "a" };
        var b = new CycleNodeSerializable { name = "b" };
        a.child = b;
        b.child = a;

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => ((ISerializable)a).CaptureState());
        Assert.Contains("Cycle reference detected", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RestoreState_WhenPrimitiveNodeTypeMismatch_Throws()
    {
        var state = new SerializingState(new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            [nameof(SampleSerializable.id)] = "not-int"
        });

        var target = new SampleSerializable();
        Assert.Throws<FormatException>(() => ((ISerializable)target).RestoreState(state));
    }

    [Fact]
    public void RestoreState_WhenNonNullableStructIsNull_Throws()
    {
        var state = new SerializingState(new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            [nameof(StructHolderSerializable.payload)] = null
        });

        var target = new StructHolderSerializable();
        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => ((ISerializable)target).RestoreState(state));
        Assert.Contains("cannot be null", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void NullablePrimitive_CaptureRestore_SupportsNullAndValue()
    {
        var sourceNull = new NullablePrimitiveSerializable { score = null };
        var restoredNull = new NullablePrimitiveSerializable();
        ((ISerializable)restoredNull).RestoreState(((ISerializable)sourceNull).CaptureState());
        Assert.Null(restoredNull.score);

        var sourceValue = new NullablePrimitiveSerializable { score = 42 };
        var restoredValue = new NullablePrimitiveSerializable();
        ((ISerializable)restoredValue).RestoreState(((ISerializable)sourceValue).CaptureState());
        Assert.Equal(42, restoredValue.score);
    }

    [Fact]
    public void CollectionCodec_Deserialize_UsesEnumerableConstructorWhenAvailable()
    {
        var source = new CtorSequenceHolderSerializable
        {
            values = new CtorSequence(new[] { 3, 5, 8 })
        };

        SerializingState state = ((ISerializable)source).CaptureState();
        var restored = new CtorSequenceHolderSerializable();
        ((ISerializable)restored).RestoreState(state);

        Assert.Equal(new[] { 3, 5, 8 }, restored.values.ToArray());
    }

    [Fact]
    public void CollectionCodec_Deserialize_UsesStaticFactoryWhenAvailable()
    {
        var source = new StaticFactorySequenceHolderSerializable
        {
            values = StaticFactorySequence.CreateRange(new[] { 1, 4, 9 })
        };

        SerializingState state = ((ISerializable)source).CaptureState();
        var restored = new StaticFactorySequenceHolderSerializable();
        ((ISerializable)restored).RestoreState(state);

        Assert.Equal(new[] { 1, 4, 9 }, restored.values.ToArray());
    }

    [Fact]
    public void MapCodec_Deserialize_UsesEnumerableConstructorWhenAvailable()
    {
        var source = new CtorMapHolderSerializable
        {
            values = new CtorMap(new[]
            {
                new KeyValuePair<string, int>("a", 10),
                new KeyValuePair<string, int>("b", 20)
            })
        };

        SerializingState state = ((ISerializable)source).CaptureState();
        var restored = new CtorMapHolderSerializable();
        ((ISerializable)restored).RestoreState(state);

        Assert.Equal(10, restored.values["a"]);
        Assert.Equal(20, restored.values["b"]);
    }

    [Fact]
    public void SerializableManager_CodecSelection_PrefersCloserAssignableCodec()
    {
        var source = new CodecDistanceHolderSerializable
        {
            entity = new DistanceLeafEntity { name = "leaf" }
        };

        SerializingState state = ((ISerializable)source).CaptureState();
        var wrapper = Assert.IsType<Dictionary<string, object?>>(state.values[nameof(CodecDistanceHolderSerializable.entity)]);
        Assert.Equal("mid", Assert.IsType<string>(wrapper["from"]));
    }

    [Fact]
    public void SerializableManager_CodecSelection_PrefersExactCodecOverFallback()
    {
        var source = new CodecExactHolderSerializable
        {
            entity = new ExactLeafEntity { value = 7 }
        };

        SerializingState state = ((ISerializable)source).CaptureState();
        var wrapper = Assert.IsType<Dictionary<string, object?>>(state.values[nameof(CodecExactHolderSerializable.entity)]);
        Assert.Equal("exact", Assert.IsType<string>(wrapper["from"]));
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

public sealed class ExternalBlob
{
    public int value;
}

public sealed class ExternalBlobCodec : SerializationCodec<ExternalBlob>
{
    public override bool CanHandleType(Type declaredType)
        => (Nullable.GetUnderlyingType(declaredType) ?? declaredType) == typeof(ExternalBlob);

    public override object? OnSerialize(in SerializeContext context, ExternalBlob value)
        => value.value;

    public override ExternalBlob OnDeserialize(in DeserializeContext context, object? node)
        => new() { value = Convert.ToInt32(node) };
}

public sealed class CodecDrivenSerializable : ISerializable
{
    [SerializableProperty]
    public ExternalBlob blob { get; set; } = new();
}

public struct PlainPayloadStruct
{
    [SerializableProperty]
    public int hp;

    [SerializableProperty]
    public int mp { get; set; }
}

public sealed class StructHolderSerializable : ISerializable
{
    [SerializableProperty]
    public PlainPayloadStruct payload;
}

public sealed class NoCodecType
{
    public int number { get; set; }
}

public sealed class NoCodecHolderSerializable : ISerializable
{
    [SerializableProperty]
    public NoCodecType value { get; set; } = new();
}

public class BaseAnimal
{
    public string name { get; set; } = string.Empty;
}

public sealed class DerivedAnimal : BaseAnimal
{
    public int level { get; set; }
}

public sealed class BaseAnimalCodec : SerializationCodec<BaseAnimal>
{
    public override bool CanHandleType(Type declaredType)
    {
        Type normalized = Nullable.GetUnderlyingType(declaredType) ?? declaredType;
        return typeof(BaseAnimal).IsAssignableFrom(normalized);
    }

    public override object? OnSerialize(in SerializeContext context, BaseAnimal value)
    {
        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["name"] = value.name,
            ["level"] = value is DerivedAnimal d ? d.level : 0
        };
    }

    public override BaseAnimal OnDeserialize(in DeserializeContext context, object? node)
    {
        Dictionary<string, object?> map = Assert.IsType<Dictionary<string, object?>>(node);
        return new DerivedAnimal
        {
            name = map.TryGetValue("name", out object? nameObj) ? (nameObj as string ?? string.Empty) : string.Empty,
            level = map.TryGetValue("level", out object? levelObj) ? Convert.ToInt32(levelObj) : 0
        };
    }
}

public sealed class InheritanceHolderSerializable : ISerializable
{
    [SerializableProperty]
    public DerivedAnimal animal { get; set; } = new();
}

public sealed class BlobListHolderSerializable : ISerializable
{
    [SerializableProperty]
    public List<ExternalBlob> values { get; set; } = new();
}

public sealed class BlobMapHolderSerializable : ISerializable
{
    [SerializableProperty]
    public Dictionary<string, ExternalBlob> values { get; set; } = new(StringComparer.Ordinal);
}

public sealed class CycleNodeSerializable : ISerializable
{
    [SerializableProperty]
    public string name { get; set; } = string.Empty;

    [SerializableProperty]
    public CycleNodeSerializable? child { get; set; }
}

public sealed class NullablePrimitiveSerializable : ISerializable
{
    [SerializableProperty]
    public int? score { get; set; }
}

public sealed class CtorSequence : IEnumerable<int>
{
    private readonly List<int> m_values;

    public CtorSequence(IEnumerable<int> values)
    {
        m_values = values.ToList();
    }

    public IEnumerator<int> GetEnumerator() => m_values.GetEnumerator();

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
}

public sealed class CtorSequenceHolderSerializable : ISerializable
{
    [SerializableProperty]
    public CtorSequence values { get; set; } = new(Array.Empty<int>());
}

public sealed class StaticFactorySequence : IEnumerable<int>
{
    private readonly List<int> m_values;

    private StaticFactorySequence(List<int> values)
    {
        m_values = values;
    }

    public static StaticFactorySequence CreateRange(IEnumerable<int> values)
    {
        return new StaticFactorySequence(values.ToList());
    }

    public IEnumerator<int> GetEnumerator() => m_values.GetEnumerator();

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
}

public sealed class StaticFactorySequenceHolderSerializable : ISerializable
{
    [SerializableProperty]
    public StaticFactorySequence values { get; set; } = StaticFactorySequence.CreateRange(Array.Empty<int>());
}

public sealed class CtorMap : IEnumerable<KeyValuePair<string, int>>
{
    private readonly Dictionary<string, int> m_values;

    public CtorMap(IEnumerable<KeyValuePair<string, int>> values)
    {
        m_values = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (KeyValuePair<string, int> pair in values)
            m_values[pair.Key] = pair.Value;
    }

    public int this[string key] => m_values[key];

    public IEnumerator<KeyValuePair<string, int>> GetEnumerator() => m_values.GetEnumerator();

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
}

public sealed class CtorMapHolderSerializable : ISerializable
{
    [SerializableProperty]
    public CtorMap values { get; set; } = new(Array.Empty<KeyValuePair<string, int>>());
}

public class DistanceRootEntity
{
    public string name { get; set; } = string.Empty;
}

public class DistanceMidEntity : DistanceRootEntity
{
}

public sealed class DistanceLeafEntity : DistanceMidEntity
{
}

public sealed class DistanceRootEntityCodec : SerializationCodec<DistanceRootEntity>
{
    public override bool CanHandleType(Type declaredType)
    {
        Type normalized = Nullable.GetUnderlyingType(declaredType) ?? declaredType;
        return typeof(DistanceRootEntity).IsAssignableFrom(normalized);
    }

    public override object? OnSerialize(in SerializeContext context, DistanceRootEntity value)
    {
        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["from"] = "root",
            ["name"] = value.name
        };
    }

    public override DistanceRootEntity OnDeserialize(in DeserializeContext context, object? node)
    {
        Dictionary<string, object?> map = Assert.IsType<Dictionary<string, object?>>(node);
        return new DistanceLeafEntity
        {
            name = map.TryGetValue("name", out object? nameObj) ? (nameObj as string ?? string.Empty) : string.Empty
        };
    }
}

public sealed class DistanceMidEntityCodec : SerializationCodec<DistanceMidEntity>
{
    public override bool CanHandleType(Type declaredType)
    {
        Type normalized = Nullable.GetUnderlyingType(declaredType) ?? declaredType;
        return typeof(DistanceMidEntity).IsAssignableFrom(normalized);
    }

    public override object? OnSerialize(in SerializeContext context, DistanceMidEntity value)
    {
        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["from"] = "mid",
            ["name"] = value.name
        };
    }

    public override DistanceMidEntity OnDeserialize(in DeserializeContext context, object? node)
    {
        Dictionary<string, object?> map = Assert.IsType<Dictionary<string, object?>>(node);
        return new DistanceLeafEntity
        {
            name = map.TryGetValue("name", out object? nameObj) ? (nameObj as string ?? string.Empty) : string.Empty
        };
    }
}

public sealed class CodecDistanceHolderSerializable : ISerializable
{
    [SerializableProperty]
    public DistanceLeafEntity entity { get; set; } = new();
}

public class ExactRootEntity
{
    public int value { get; set; }
}

public sealed class ExactLeafEntity : ExactRootEntity
{
}

public sealed class ExactRootEntityCodec : SerializationCodec<ExactRootEntity>
{
    public override bool CanHandleType(Type declaredType)
    {
        Type normalized = Nullable.GetUnderlyingType(declaredType) ?? declaredType;
        return typeof(ExactRootEntity).IsAssignableFrom(normalized);
    }

    public override object? OnSerialize(in SerializeContext context, ExactRootEntity value)
    {
        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["from"] = "root-fallback",
            ["value"] = value.value
        };
    }

    public override ExactRootEntity OnDeserialize(in DeserializeContext context, object? node)
    {
        Dictionary<string, object?> map = Assert.IsType<Dictionary<string, object?>>(node);
        return new ExactLeafEntity
        {
            value = map.TryGetValue("value", out object? valueObj) ? Convert.ToInt32(valueObj) : 0
        };
    }
}

public sealed class ExactLeafEntityCodec : SerializationCodec<ExactLeafEntity>
{
    public override bool CanHandleType(Type declaredType)
    {
        Type normalized = Nullable.GetUnderlyingType(declaredType) ?? declaredType;
        return normalized == typeof(ExactLeafEntity);
    }

    public override object? OnSerialize(in SerializeContext context, ExactLeafEntity value)
    {
        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["from"] = "exact",
            ["value"] = value.value
        };
    }

    public override ExactLeafEntity OnDeserialize(in DeserializeContext context, object? node)
    {
        Dictionary<string, object?> map = Assert.IsType<Dictionary<string, object?>>(node);
        return new ExactLeafEntity
        {
            value = map.TryGetValue("value", out object? valueObj) ? Convert.ToInt32(valueObj) : 0
        };
    }
}

public sealed class CodecExactHolderSerializable : ISerializable
{
    [SerializableProperty]
    public ExactLeafEntity entity { get; set; } = new();
}
