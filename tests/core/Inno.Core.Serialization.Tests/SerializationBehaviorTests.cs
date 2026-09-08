using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

using Inno.Extensibility.Modules;
using Inno.Extensibility.Types;
using Inno.Core.Serialization.Converters;

using Xunit;

namespace Inno.Core.Serialization.Tests;

public sealed class SerializationBehaviorTests : IDisposable
{
    private readonly string m_testRoot = Path.Combine(
        Path.GetTempPath(),
        "InnoSerializationTests",
        Guid.NewGuid().ToString("N"));
    private ModuleHost m_modules;
    private TypeCatalog m_types;
    private SerializationRegistry m_serialization;

    public SerializationBehaviorTests()
    {
        m_modules = new ModuleHost(new ModuleHostOptions
        {
            cacheDirectory = Path.Combine(m_testRoot, "Assemblies")
        });
        m_types = new TypeCatalog(m_modules);
        m_serialization = new SerializationRegistry(m_types);
    }

    public void Dispose()
    {
        m_serialization.Dispose();
        m_types.Dispose();
        m_modules.Dispose();
        if (Directory.Exists(m_testRoot))
            Directory.Delete(m_testRoot, recursive: true);
    }

    [Fact]
    public void ISerializable_IsPureMarkerInterface()
    {
        Assert.Empty(typeof(ISerializable).GetMethods());
        Assert.Empty(typeof(ISerializable).GetProperties());
    }

    [Fact]
    public void PublicOperations_RejectDisposedRegistry()
    {
        m_serialization.Dispose();

        Assert.Throws<ObjectDisposedException>(
            () => m_serialization.Serialize(new DefaultSample()));

        m_serialization = new SerializationRegistry(m_types);
        Assert.NotEmpty(m_serialization.Serialize(new DefaultSample()));
    }

    [Fact]
    public async Task CapturedGeneration_RemainsUsableAcrossRefreshAndWorkerContinuation()
    {
        var source = new DefaultSample { count = 73, name = "Pinned" };
        SerializationGeneration generation = m_serialization.CaptureGeneration();

        m_types.Rebuild();
        byte[] bytes = await Task.Run(() => generation.Serialize(source));
        DefaultSample restored = await Task.Run(() => generation.Deserialize<DefaultSample>(bytes));

        Assert.Equal(73, restored.count);
        Assert.Equal("Pinned", restored.name);
        generation.Dispose();
        Assert.Throws<ObjectDisposedException>(() => generation.Serialize(source));
    }

    [Fact]
    public void TypeCatalogConstructor_RequiresActiveModuleHost()
    {
        m_serialization.Dispose();
        m_types.Dispose();
        m_modules.Dispose();

        Assert.Throws<InvalidOperationException>(() => new TypeCatalog(m_modules));

        m_modules = new ModuleHost(new ModuleHostOptions
        {
            cacheDirectory = Path.Combine(m_testRoot, "ReplacementAssemblies")
        });
        m_types = new TypeCatalog(m_modules);
        m_serialization = new SerializationRegistry(m_types);
    }

    [Fact]
    public void SerializeDeserialize_RoundTripsSupportedDefaultValues()
    {
        var source = new DefaultSample
        {
            enabled = true,
            count = 42,
            ratio = 0.75f,
            price = 19.25m,
            name = "Player",
            identity = Guid.Parse("85228e75-032a-4056-a92f-358fc0fdba14"),
            mode = SampleMode.Hard,
            optional = 8,
            bytes = [1, 2, 3],
            numbers = [4, 5, 6],
            values = [7, 8],
            map = new Dictionary<int, string> { [2] = "two", [1] = "one" },
            data = StructSample.Create(11, 12, 13)
        };

        byte[] bytes = m_serialization.Serialize(source);
        DefaultSample restored = m_serialization.Deserialize<DefaultSample>(bytes);

        Assert.True(restored.enabled);
        Assert.Equal(42, restored.count);
        Assert.Equal(0.75f, restored.ratio);
        Assert.Equal(19.25m, restored.price);
        Assert.Equal("Player", restored.name);
        Assert.Equal(source.identity, restored.identity);
        Assert.Equal(SampleMode.Hard, restored.mode);
        Assert.Equal(8, restored.optional);
        Assert.Equal(new byte[] { 1, 2, 3 }, restored.bytes);
        Assert.Equal(new[] { 4, 5, 6 }, restored.numbers);
        Assert.Equal(new[] { 7, 8 }, restored.values);
        Assert.Equal("one", restored.map[1]);
        Assert.Equal("two", restored.map[2]);
        Assert.Equal(11, restored.data.x);
        Assert.Equal(12, restored.data.y);
        Assert.Equal(13, restored.data.hidden);
        Assert.Equal(0, restored.data.readOnly);
        Assert.Equal(24, restored.data.computed);
    }

    [Fact]
    public void TypeRefSerializationPersistsOnlyStableIdentity()
    {
        TypeRef active = m_types.GetTypeRef(typeof(DefaultSample));

        byte[] bytes = m_serialization.Encode(writer => writer.Write("type", active));
        TypeRef restored = m_serialization.Decode(bytes, reader =>
        {
            SerializationReader type = reader.ReadObject("type");
            Assert.True(type.Contains("stableId"));
            Assert.False(type.Contains("runtimeId"));
            Assert.False(type.Contains("isValid"));
            return reader.Read<TypeRef>("type");
        });

        Assert.Equal(active, restored);
        Assert.Equal(0, restored.runtimeId);
        Assert.Equal(active.Resolve(m_types), restored.Resolve(m_types));
    }

    [Fact]
    public void Restore_UpdatesExistingInstance()
    {
        var source = new DefaultSample { count = 77, name = "Restored" };
        var target = new DefaultSample { count = 1, name = "Old" };

        m_serialization.Restore(target, m_serialization.Serialize(source));

        Assert.Equal(77, target.count);
        Assert.Equal("Restored", target.name);
    }

    [Fact]
    public void CollectingPropertyRestore_CollectsInvalidMembersAndPreservesDefaults()
    {
        var previous = new PreviousSchemaSample
        {
            changed = 42,
            compatible = "preserved",
            removed = 9
        };
        IReadOnlyList<SerializationPropertySnapshot> snapshots =
            m_serialization.CaptureProperties(previous);
        var current = new CurrentSchemaSample();

        SerializationPropertyRestoreResult result = m_serialization.RestoreProperties(
            current,
            snapshots,
            SerializationPropertyRestoreMode.CollectFailures);

        Assert.False(result.success);
        Assert.Equal(1, result.restoredCount);
        Assert.Equal(1, result.ignoredCount);
        SerializationPropertyRestoreFailure failure = Assert.Single(result.failures);
        Assert.Equal(nameof(PreviousSchemaSample.changed), failure.name);
        Assert.Equal(typeof(int), failure.previousPropertyType);
        Assert.Equal(typeof(string), failure.currentPropertyType);
        Assert.Equal("default", current.changed);
        Assert.Equal("preserved", current.compatible);
        Assert.Equal(17, current.added);
        Assert.Equal(1, current.restoreCount);
    }

    [Fact]
    public void StrictPropertyRestore_ThrowsForAnIncompatibleMember()
    {
        IReadOnlyList<SerializationPropertySnapshot> snapshots =
            m_serialization.CaptureProperties(new PreviousSchemaSample { changed = 42 });
        var current = new CurrentSchemaSample();

        Assert.Throws<InvalidOperationException>(() => m_serialization.RestoreProperties(
            current,
            snapshots,
            SerializationPropertyRestoreMode.Strict));

        Assert.Equal("default", current.changed);
        Assert.Equal(0, current.restoreCount);
    }

    [Fact]
    public void PropertyData_CapturesAndRestoresOnlyTheRequestedMember()
    {
        var source = new DefaultSample
        {
            count = 81,
            name = "Captured"
        };
        byte[] data = m_serialization.CapturePropertyData(source, nameof(DefaultSample.count));
        var target = new DefaultSample
        {
            count = 3,
            name = "Preserved"
        };

        SerializationPropertyRestoreResult result = m_serialization.RestorePropertiesData(target, data);

        Assert.True(result.success);
        Assert.Equal(1, result.restoredCount);
        Assert.Equal(81, target.count);
        Assert.Equal("Preserved", target.name);
    }

    [Fact]
    public void PropertyData_RoundTripsAllPersistentMembersWithoutTheOwningObject()
    {
        var source = new PreviousSchemaSample
        {
            changed = 19,
            compatible = "Value",
            removed = 27
        };
        byte[] data = m_serialization.CapturePropertiesData(source);
        var target = new PreviousSchemaSample();

        SerializationPropertyRestoreResult result = m_serialization.RestorePropertiesData(target, data);

        Assert.True(result.success);
        Assert.Equal(3, result.restoredCount);
        Assert.Equal(19, target.changed);
        Assert.Equal("Value", target.compatible);
        Assert.Equal(27, target.removed);
    }

    [Fact]
    public void PropertyData_RejectsMalformedOrTrailingBytes()
    {
        byte[] data = m_serialization.CapturePropertyData(
            new DefaultSample { count = 12 },
            nameof(DefaultSample.count));
        byte[] trailing = new byte[data.Length + 1];
        data.CopyTo(trailing, 0);

        Assert.Throws<InvalidDataException>(() => m_serialization.RestorePropertiesData(
            new DefaultSample(),
            trailing));
        Assert.ThrowsAny<Exception>(() => m_serialization.RestorePropertiesData(
            new DefaultSample(),
            new byte[] { 1, 2, 3 }));
    }

    [Fact]
    public void Deserialize_UsesNonPublicParameterlessConstructor()
    {
        byte[] bytes = m_serialization.Encode(static writer => writer.Write("value", 9));

        PrivateConstructorSample restored = m_serialization.Deserialize<PrivateConstructorSample>(bytes);

        Assert.True(restored.wasConstructed);
        Assert.Equal(9, restored.value);
    }

    [Fact]
    public void Deserialize_WithoutParameterlessConstructor_RequiresConverter()
    {
        byte[] bytes = m_serialization.Encode(static writer => writer.Write("value", 5));

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => m_serialization.Deserialize<MissingConstructorSample>(bytes));

        Assert.Contains(typeof(MissingConstructorSample).FullName!, exception.Message);
        Assert.Contains("parameterless constructor", exception.Message);
    }

    [Fact]
    public void Deserialize_UsesConverterWhenNoParameterlessConstructorExists()
    {
        var source = new ConvertedConstructorSample(31);

        ConvertedConstructorSample restored = m_serialization.Deserialize<ConvertedConstructorSample>(
            m_serialization.Serialize(source));

        Assert.Equal(31, restored.value);
    }

    [Fact]
    public void Deserialize_PreservesConstructorFailureAsInnerException()
    {
        byte[] bytes = m_serialization.Encode(static _ => { });

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => m_serialization.Deserialize<ThrowingConstructorSample>(bytes));

        Assert.IsType<ApplicationException>(exception.InnerException);
        Assert.Equal("constructor failure", exception.InnerException!.Message);
    }

    [Fact]
    public void RequiredConverter_Missing_ThrowsClearError()
    {
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => m_serialization.Serialize(new MissingRequiredConverterSample()));

        Assert.Contains(typeof(MissingRequiredConverterSample).FullName!, exception.Message);
        Assert.Contains("requires an explicit serialization converter", exception.Message);
    }

    [Fact]
    public void Visibility_ControlsPersistenceAndRuntimeMetadataStrictly()
    {
        var source = new VisibilitySample
        {
            shown = 1,
            hidden = 2,
            readOnly = 3,
            transient = 4,
            serializeOnly = 5,
            deserializeOnly = 6
        };

        VisibilitySample restored = m_serialization.Deserialize<VisibilitySample>(m_serialization.Serialize(source));
        Assert.Equal(1, restored.shown);
        Assert.Equal(2, restored.hidden);
        Assert.Equal(3, restored.readOnly);
        Assert.Equal(0, restored.transient);
        Assert.Equal(0, restored.serializeOnly);
        Assert.Equal(0, restored.deserializeOnly);

        IReadOnlyList<SerializedProperty> properties = m_serialization.GetProperties(source);
        Assert.DoesNotContain(properties, property => property.name == nameof(VisibilitySample.hidden));
        SerializedProperty shown = properties.Single(property => property.name == nameof(VisibilitySample.shown));
        Assert.True(shown.canRead);
        Assert.True(shown.canWrite);
        shown.SetValue(10);
        Assert.Equal(10, source.shown);

        SerializedProperty readOnly = properties.Single(property => property.name == nameof(VisibilitySample.readOnly));
        Assert.True(readOnly.canRead);
        Assert.False(readOnly.canWrite);
        Assert.Throws<InvalidOperationException>(() => readOnly.SetValue(12));

        byte[] manual = m_serialization.Encode(static writer =>
        {
            writer.Write(nameof(VisibilitySample.deserializeOnly), 99);
            writer.Write(nameof(VisibilitySample.serializeOnly), 98);
        });
        _ = m_serialization.Decode(manual, reader =>
        {
            reader.RestoreProperties(source);
            return true;
        });
        Assert.Equal(99, source.deserializeOnly);
        Assert.Equal(5, source.serializeOnly);
    }

    [Fact]
    public void MetadataValidation_RejectsInvalidMembersAndDuplicateKeys()
    {
        Assert.Throws<InvalidOperationException>(
            () => m_serialization.GetProperties(new MissingSetterSample()));
        Assert.Throws<InvalidOperationException>(
            () => m_serialization.GetProperties(new DuplicateKeyDerivedSample()));
    }

    [Fact]
    public void ClassMemberWithoutConverter_FailsWithCompletePath()
    {
        var source = new MissingClassHost { child = new MissingChild { value = 1 } };

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => m_serialization.Serialize(source));

        Assert.Contains("$.child", exception.Message);
        Assert.Contains("SerializationConverter", exception.Message);
    }

    [Fact]
    public void ClassCollectionElementWithoutConverter_FailsWithIndexedPath()
    {
        var source = new MissingClassCollectionHost
        {
            children = [new MissingChild { value = 2 }]
        };

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => m_serialization.Serialize(source));

        Assert.Contains("$.children[0]", exception.Message);
    }

    [Fact]
    public void ExplicitConverter_RoundTripsNestedClass()
    {
        var source = new ConvertedChildHost
        {
            child = new ConvertedChild { value = 14 }
        };

        ConvertedChildHost restored = m_serialization.Deserialize<ConvertedChildHost>(
            m_serialization.Serialize(source));

        Assert.NotNull(restored.child);
        Assert.Equal(14, restored.child!.value);
    }

    [Fact]
    public void ExplicitConverter_CycleFailsWithBothPaths()
    {
        var cycle = new CycleValue();
        cycle.next = cycle;
        var source = new CycleHost { value = cycle };

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => m_serialization.Serialize(source));

        Assert.Contains("Serialization cycle detected", exception.Message);
        Assert.Contains("$.value", exception.Message);
        Assert.Contains("$.value.next", exception.Message);
    }

    [Fact]
    public void ConverterSelection_PrefersNearestBaseThenExactType()
    {
        var nearestSource = new NearestHost { item = new NearestLeaf { value = 21 } };
        NearestHost nearest = m_serialization.Deserialize<NearestHost>(m_serialization.Serialize(nearestSource));
        Assert.Equal("mid", nearest.item!.selectedBy);
        Assert.Equal(21, nearest.item.value);

        var exactSource = new ExactHost { item = new ExactLeaf { value = 22 } };
        ExactHost exact = m_serialization.Deserialize<ExactHost>(m_serialization.Serialize(exactSource));
        Assert.Equal("exact", exact.item!.selectedBy);
        Assert.Equal(22, exact.item.value);
    }

    [Fact]
    public void ConverterSelection_RejectsEqualDistanceAmbiguity()
    {
        var source = new AmbiguousHost { item = new AmbiguousValue() };

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => m_serialization.Serialize(source));

        Assert.Contains("ambiguous converters", exception.Message);
        Assert.Contains(typeof(AmbiguousValue).FullName!, exception.Message);
    }

    [Fact]
    public void OpenGenericConverter_IsClosedFromRequestedType()
    {
        var source = new GenericBoxHost { box = new GenericBox<int>(45) };

        GenericBoxHost restored = m_serialization.Deserialize<GenericBoxHost>(m_serialization.Serialize(source));

        Assert.NotNull(restored.box);
        Assert.Equal(45, restored.box!.value);
    }

    [Fact]
    public void SerializationContext_IsImmutableAndUsesExactTypeKeys()
    {
        var service = new SampleContext();
        SerializationContext original = SerializationContext.empty;
        SerializationContext updated = original.With<ISampleContext>(service);

        Assert.False(original.TryGet<ISampleContext>(out _));
        Assert.True(updated.TryGet<ISampleContext>(out ISampleContext? resolved));
        Assert.Same(service, resolved);
        Assert.False(updated.TryGet<SampleContext>(out _));
        Assert.Same(service, updated.GetRequired<ISampleContext>());
        Assert.Throws<InvalidOperationException>(() => updated.GetRequired<SampleContext>());
    }

    [Fact]
    public void ReaderWriter_ReportDuplicateMissingAndTypePaths()
    {
        InvalidOperationException duplicate = Assert.Throws<InvalidOperationException>(() =>
            m_serialization.Encode(writer =>
            {
                writer.Write("value", 1);
                writer.Write("value", 2);
            }));
        Assert.Contains("$", duplicate.Message);
        Assert.Contains("value", duplicate.Message);

        byte[] arrayBytes = m_serialization.Encode(writer =>
            writer.WriteObjectArray("items", new[] { 1 }, static (_, _) => { }));
        InvalidOperationException missing = Assert.Throws<InvalidOperationException>(() =>
            m_serialization.Decode(arrayBytes, static reader =>
                reader.ReadObjectArray("items")[0].Read<int>("missing")));
        Assert.Contains("$.items[0].missing", missing.Message);

        byte[] valueBytes = m_serialization.Encode(static writer => writer.Write("value", "text"));
        InvalidOperationException mismatch = Assert.Throws<InvalidOperationException>(() =>
            m_serialization.Decode(valueBytes, static reader => reader.Read<int>("value")));
        Assert.Contains("$.value", mismatch.Message);

        InvalidOperationException tryReadMismatch = Assert.Throws<InvalidOperationException>(() =>
            m_serialization.Decode(valueBytes, static reader => reader.TryRead<int>("value", out _)));
        Assert.Contains("$.value", tryReadMismatch.Message);
        Assert.False(m_serialization.Decode(valueBytes, static reader => reader.TryRead<int>("missing", out _)));
    }

    [Fact]
    public void ReaderWriter_AreInvalidOutsideTheirOperation()
    {
        SerializationWriter? capturedWriter = null;
        _ = m_serialization.Encode(writer => capturedWriter = writer);
        Assert.Throws<InvalidOperationException>(() => capturedWriter!.Write("late", 1));

        byte[] bytes = m_serialization.Encode(static writer => writer.Write("value", 1));
        SerializationReader? capturedReader = null;
        _ = m_serialization.Decode(bytes, reader =>
        {
            capturedReader = reader;
            return true;
        });
        Assert.Throws<InvalidOperationException>(() => capturedReader!.Contains("value"));
    }

    [Fact]
    public void RestoreHooks_RunOnceBaseToDerivedForDefaultAndConverterPaths()
    {
        byte[] bytes = m_serialization.Encode(static writer => writer.Write("value", 3));
        var target = new HookDerivedSample();
        _ = m_serialization.Decode(bytes, reader =>
        {
            reader.RestoreProperties(target);
            reader.RestoreProperties(target);
            return true;
        });
        Assert.Equal(new[] { "base", "derived" }, target.calls);

        var converted = new ConvertedHookSample { value = 8 };
        ConvertedHookSample restored = m_serialization.Deserialize<ConvertedHookSample>(
            m_serialization.Serialize(converted));
        Assert.Equal(1, restored.hookCount);

        var existing = new ConvertedHookSample();
        m_serialization.Restore(existing, m_serialization.Serialize(converted));
        Assert.Equal(8, existing.value);
        Assert.Equal(1, existing.hookCount);
    }

    [Fact]
    public void CompletionCallbacks_RunOnlyAfterSuccessfulDecode()
    {
        byte[] bytes = m_serialization.Encode(static writer => writer.Write("value", 1));
        var calls = new List<string>();
        int value = m_serialization.Decode(bytes, reader =>
        {
            reader.OnCompleted(() => calls.Add("completed"));
            Assert.Empty(calls);
            return reader.Read<int>("value");
        });
        Assert.Equal(1, value);
        Assert.Equal(new[] { "completed" }, calls);

        calls.Clear();
        Assert.Throws<InvalidOperationException>(() => m_serialization.Decode(bytes, reader =>
        {
            reader.OnCompleted(() => calls.Add("should-not-run"));
            return reader.Read<int>("missing");
        }));
        Assert.Empty(calls);
    }

    [Fact]
    public void NonStringMapEncoding_IsDeterministic()
    {
        var left = new MapHost { values = new Dictionary<int, string> { [9] = "nine", [1] = "one" } };
        var right = new MapHost { values = new Dictionary<int, string> { [1] = "one", [9] = "nine" } };

        Assert.Equal(m_serialization.Serialize(left), m_serialization.Serialize(right));
    }

    [Fact]
    public void ForeignFormatHeader_IsRejected()
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
            writer.Write("FOREIGN");

        InvalidDataException exception = Assert.Throws<InvalidDataException>(
            () => m_serialization.Decode(stream.ToArray(), static _ => true));
        Assert.Contains("Invalid serialization magic", exception.Message);
    }
}

internal enum SampleMode
{
    Easy,
    Hard
}

internal sealed class PreviousSchemaSample : ISerializable
{
    [SerializableProperty] public int changed { get; set; }
    [SerializableProperty(PropertyVisibility.Hide)] public string compatible { get; set; } = string.Empty;
    [SerializableProperty] public int removed { get; set; }
}

internal sealed class CurrentSchemaSample : ISerializable
{
    [SerializableProperty] public string changed { get; set; } = "default";
    [SerializableProperty(PropertyVisibility.Hide)] public string compatible { get; set; } = "new";
    [SerializableProperty] public int added { get; set; } = 17;
    public int restoreCount { get; private set; }

    [OnSerializableRestored]
    private void AfterRestore() => restoreCount++;
}

internal sealed class DefaultSample : ISerializable
{
    [SerializableProperty] public bool enabled { get; set; }
    [SerializableProperty] public int count { get; set; }
    [SerializableProperty] public float ratio { get; set; }
    [SerializableProperty] public decimal price { get; set; }
    [SerializableProperty] public string name { get; set; } = string.Empty;
    [SerializableProperty] public Guid identity { get; set; }
    [SerializableProperty] public SampleMode mode { get; set; }
    [SerializableProperty] public int? optional { get; set; }
    [SerializableProperty] public byte[] bytes { get; set; } = [];
    [SerializableProperty] public int[] numbers { get; set; } = [];
    [SerializableProperty] public List<int> values { get; set; } = [];
    [SerializableProperty] public Dictionary<int, string> map { get; set; } = [];
    [SerializableProperty] public StructSample data { get; set; }
}

internal struct StructSample
{
    public int x;
    public int y { get; set; }
    public readonly int readOnly;
    [SerializableProperty] private int m_hidden;
    public readonly int computed => x + m_hidden;
    public readonly int hidden => m_hidden;

    private StructSample(int x, int y, int hidden)
    {
        this.x = x;
        this.y = y;
        readOnly = 99;
        m_hidden = hidden;
    }

    internal static StructSample Create(int x, int y, int hidden) => new(x, y, hidden);
}

internal sealed class PrivateConstructorSample : ISerializable
{
    [SerializableProperty] public int value { get; set; }
    public bool wasConstructed { get; }

    private PrivateConstructorSample()
    {
        wasConstructed = true;
    }
}

internal sealed class MissingConstructorSample : ISerializable
{
    [SerializableProperty] public int value { get; set; }

    public MissingConstructorSample(int value)
    {
        this.value = value;
    }
}

internal sealed class ConvertedConstructorSample : ISerializable
{
    public int value { get; set; }

    public ConvertedConstructorSample(int value)
    {
        this.value = value;
    }
}

[SerializationExtension]
internal sealed class ConvertedConstructorSampleConverter : SerializationConverter<ConvertedConstructorSample>
{
    public override void Write(SerializationWriter writer, ConvertedConstructorSample value)
        => writer.Write("value", value.value);

    public override ConvertedConstructorSample Read(SerializationReader reader)
        => new(reader.Read<int>("value"));

    public override void Restore(SerializationReader reader, ConvertedConstructorSample target)
        => target.value = reader.Read<int>("value");
}

internal sealed class ThrowingConstructorSample : ISerializable
{
    private ThrowingConstructorSample()
    {
        throw new ApplicationException("constructor failure");
    }
}

[RequiresSerializationConverter]
internal sealed class MissingRequiredConverterSample : ISerializable;

internal sealed class VisibilitySample : ISerializable
{
    [SerializableProperty] public int shown { get; set; }
    [SerializableProperty(PropertyVisibility.Hide)] public int hidden { get; set; }
    [SerializableProperty(PropertyVisibility.Readonly)] public int readOnly { get; set; }
    [SerializableProperty(PropertyVisibility.Transient)] public int transient { get; set; }
    [SerializableProperty(PropertyVisibility.SerializeOnly)] public int serializeOnly { get; set; }
    [SerializableProperty(PropertyVisibility.DeserializeOnly)] public int deserializeOnly { get; set; }
}

internal sealed class MissingSetterSample : ISerializable
{
    [SerializableProperty]
    public int value { get; } = 1;
}

internal class DuplicateKeyBaseSample : ISerializable
{
    [SerializableProperty]
    public int value { get; set; }
}

internal sealed class DuplicateKeyDerivedSample : DuplicateKeyBaseSample
{
    [SerializableProperty]
    public new int value { get; set; }
}

internal sealed class MissingChild
{
    public int value { get; set; }
}

internal sealed class MissingClassHost : ISerializable
{
    [SerializableProperty] public MissingChild? child { get; set; }
}

internal sealed class MissingClassCollectionHost : ISerializable
{
    [SerializableProperty] public List<MissingChild> children { get; set; } = [];
}

internal sealed class ConvertedChild
{
    public int value { get; set; }
}

internal sealed class ConvertedChildHost : ISerializable
{
    [SerializableProperty] public ConvertedChild? child { get; set; }
}

internal sealed class CycleValue
{
    public CycleValue? next { get; set; }
}

internal sealed class CycleHost : ISerializable
{
    [SerializableProperty] public CycleValue? value { get; set; }
}

[SerializationExtension]
internal sealed class CycleValueConverter : SerializationConverter<CycleValue>
{
    public override void Write(SerializationWriter writer, CycleValue value)
        => writer.Write("next", value.next);

    public override CycleValue Read(SerializationReader reader)
        => new() { next = reader.Read<CycleValue?>("next") };
}

[SerializationExtension]
internal sealed class ConvertedChildConverter : SerializationConverter<ConvertedChild>
{
    public override void Write(SerializationWriter writer, ConvertedChild value)
        => writer.Write("value", value.value);

    public override ConvertedChild Read(SerializationReader reader)
        => new() { value = reader.Read<int>("value") };
}

internal class NearestBase
{
    public int value { get; set; }
    public string selectedBy { get; set; } = string.Empty;
}

internal class NearestMid : NearestBase;

internal sealed class NearestLeaf : NearestMid;

internal sealed class NearestHost : ISerializable
{
    [SerializableProperty] public NearestLeaf? item { get; set; }
}

[SerializationExtension]
internal sealed class NearestBaseConverter : SerializationConverter<NearestBase>
{
    public override void Write(SerializationWriter writer, NearestBase value)
        => writer.Write("value", value.value);

    public override NearestBase Read(SerializationReader reader)
        => new NearestLeaf { value = reader.Read<int>("value"), selectedBy = "base" };
}

[SerializationExtension]
internal sealed class NearestMidConverter : SerializationConverter<NearestMid>
{
    public override void Write(SerializationWriter writer, NearestMid value)
        => writer.Write("value", value.value);

    public override NearestMid Read(SerializationReader reader)
        => new NearestLeaf { value = reader.Read<int>("value"), selectedBy = "mid" };
}

internal class ExactBase
{
    public int value { get; set; }
    public string selectedBy { get; set; } = string.Empty;
}

internal sealed class ExactLeaf : ExactBase;

internal sealed class ExactHost : ISerializable
{
    [SerializableProperty] public ExactLeaf? item { get; set; }
}

[SerializationExtension]
internal sealed class ExactBaseConverter : SerializationConverter<ExactBase>
{
    public override void Write(SerializationWriter writer, ExactBase value)
        => writer.Write("value", value.value);

    public override ExactBase Read(SerializationReader reader)
        => new ExactLeaf { value = reader.Read<int>("value"), selectedBy = "base" };
}

[SerializationExtension]
internal sealed class ExactLeafConverter : SerializationConverter<ExactLeaf>
{
    public override void Write(SerializationWriter writer, ExactLeaf value)
        => writer.Write("value", value.value);

    public override ExactLeaf Read(SerializationReader reader)
        => new() { value = reader.Read<int>("value"), selectedBy = "exact" };
}

internal interface IAmbiguousLeft;

internal interface IAmbiguousRight;

internal sealed class AmbiguousValue : IAmbiguousLeft, IAmbiguousRight;

internal sealed class AmbiguousHost : ISerializable
{
    [SerializableProperty] public AmbiguousValue? item { get; set; }
}

[SerializationExtension]
internal sealed class AmbiguousLeftConverter : SerializationConverter<IAmbiguousLeft>
{
    public override void Write(SerializationWriter writer, IAmbiguousLeft value) { }
    public override IAmbiguousLeft Read(SerializationReader reader) => new AmbiguousValue();
}

[SerializationExtension]
internal sealed class AmbiguousRightConverter : SerializationConverter<IAmbiguousRight>
{
    public override void Write(SerializationWriter writer, IAmbiguousRight value) { }
    public override IAmbiguousRight Read(SerializationReader reader) => new AmbiguousValue();
}

internal sealed class GenericBox<T>
{
    public T value { get; }

    public GenericBox(T value)
    {
        this.value = value;
    }
}

internal sealed class GenericBoxHost : ISerializable
{
    [SerializableProperty] public GenericBox<int>? box { get; set; }
}

[SerializationExtension]
internal sealed class GenericBoxConverter<T> : SerializationConverter<GenericBox<T>>
{
    public override void Write(SerializationWriter writer, GenericBox<T> value)
        => writer.Write("value", value.value);

    public override GenericBox<T> Read(SerializationReader reader)
        => new(reader.Read<T>("value"));
}

internal interface ISampleContext;

internal sealed class SampleContext : ISampleContext;

internal class HookBaseSample : ISerializable
{
    [SerializableProperty] public int value { get; set; }
    public List<string> calls { get; } = [];

    [OnSerializableRestored]
    private void AfterBaseRestore() => calls.Add("base");
}

internal sealed class HookDerivedSample : HookBaseSample
{
    [OnSerializableRestored]
    private void AfterDerivedRestore() => calls.Add("derived");
}

internal sealed class ConvertedHookSample : ISerializable
{
    public int value { get; set; }
    public int hookCount { get; private set; }

    [OnSerializableRestored]
    private void AfterRestore() => hookCount++;
}

[SerializationExtension]
internal sealed class ConvertedHookSampleConverter : SerializationConverter<ConvertedHookSample>
{
    public override void Write(SerializationWriter writer, ConvertedHookSample value)
        => writer.Write("value", value.value);

    public override ConvertedHookSample Read(SerializationReader reader)
        => new() { value = reader.Read<int>("value") };

    public override void Restore(SerializationReader reader, ConvertedHookSample target)
        => target.value = reader.Read<int>("value");
}

internal sealed class MapHost : ISerializable
{
    [SerializableProperty]
    public Dictionary<int, string> values { get; set; } = [];
}
