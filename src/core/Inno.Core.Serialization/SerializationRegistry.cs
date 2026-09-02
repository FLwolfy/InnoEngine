using System;
using System.Collections.Generic;
using System.IO;
using Inno.Extensibility.Types;

namespace Inno.Core.Serialization;

/// <summary>
/// Owns one generation-aware converter registry and provides deterministic root serialization operations.
/// </summary>
public sealed class SerializationRegistry : IDisposable
{
    private readonly ConverterRegistry m_converters;
    private readonly TypeCatalog m_types;
    private bool m_disposed;

    /// <summary>
    /// Creates a serialization registry derived from one isolated type catalog.
    /// </summary>
    /// <param name="types">
    /// The type catalog that owns converter extension generations.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="types"/> is null.
    /// </exception>
    public SerializationRegistry(TypeCatalog types)
    {
        ArgumentNullException.ThrowIfNull(types);
        m_types = types;
        m_converters = new ConverterRegistry(types);
    }

    /// <summary>
    /// Releases every converter generation owned by this registry.
    /// </summary>
    public void Dispose()
    {
        if (m_disposed)
            return;
        m_disposed = true;
        m_converters.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Captures the currently active converter generation for deterministic work that may cross
    /// asynchronous continuations.
    /// </summary>
    /// <returns>
    /// An immutable serialization generation owned by the caller.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when serialization services have not been initialized.
    /// </exception>
    public SerializationGeneration CaptureGeneration()
    {
        EnsureInitialized();
        return new SerializationGeneration(this, m_converters.Capture());
    }

    /// <summary>
    /// Gets the stable ordered runtime-visible properties for a serializable object.
    /// </summary>
    /// <param name="value">
    /// The object whose property metadata should be created.
    /// </param>
    /// <returns>
    /// The properties that permit runtime reads.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="value"/> is null.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the manager is not initialized.
    /// </exception>
    public IReadOnlyList<SerializedProperty> GetProperties(ISerializable value)
    {
        EnsureInitialized();
        ArgumentNullException.ThrowIfNull(value);
        return ReflectionMetadata.GetRuntimeProperties(value);
    }

    /// <summary>
    /// Captures each persistent property independently for reload-safe state restoration.
    /// </summary>
    /// <param name="value">
    /// The object whose serializable properties should be captured.
    /// </param>
    /// <param name="context">
    /// Optional immutable converter context.
    /// </param>
    /// <returns>
    /// Ordered independent property snapshots.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="value"/> is null.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the manager is not initialized.
    /// </exception>
    public IReadOnlyList<SerializationPropertySnapshot> CaptureProperties(
        ISerializable value,
        SerializationContext? context = null)
    {
        EnsureInitialized();
        ArgumentNullException.ThrowIfNull(value);
        using ConverterRegistryLease converters = m_converters.Capture();
        return PropertySnapshotPipeline.Capture(value, CreateContext(context), converters);
    }

    /// <summary>
    /// Captures one persistent property as independently restorable neutral bytes.
    /// </summary>
    /// <param name="value">
    /// The serializable object containing the requested property.
    /// </param>
    /// <param name="propertyName">
    /// The exact serialized member key to capture.
    /// </param>
    /// <param name="context">
    /// Optional immutable converter context.
    /// </param>
    /// <returns>
    /// Neutral bytes containing the property name and encoded value.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="value"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="propertyName"/> is empty or does not identify a persistent property.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the manager is not initialized.
    /// </exception>
    public byte[] CapturePropertyData(
        ISerializable value,
        string propertyName,
        SerializationContext? context = null)
    {
        EnsureInitialized();
        ArgumentNullException.ThrowIfNull(value);
        using ConverterRegistryLease converters = m_converters.Capture();
        SerializationPropertySnapshot snapshot = PropertySnapshotPipeline.CaptureProperty(
            value,
            propertyName,
            CreateContext(context),
            converters);
        return PropertySnapshotBinaryFormat.Encode([snapshot]);
    }

    /// <summary>
    /// Captures every persistent property as neutral bytes without serializing the owning object graph.
    /// </summary>
    /// <param name="value">
    /// The serializable object whose persistent properties should be captured.
    /// </param>
    /// <param name="context">
    /// Optional immutable converter context.
    /// </param>
    /// <returns>
    /// Strictly validated bytes containing independently encoded property values.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="value"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the manager is not initialized.
    /// </exception>
    public byte[] CapturePropertiesData(
        ISerializable value,
        SerializationContext? context = null)
    {
        EnsureInitialized();
        ArgumentNullException.ThrowIfNull(value);
        using ConverterRegistryLease converters = m_converters.Capture();
        return PropertySnapshotBinaryFormat.Encode(
            PropertySnapshotPipeline.Capture(value, CreateContext(context), converters));
    }

    /// <summary>
    /// Restores independently captured properties into an existing object.
    /// </summary>
    /// <param name="target">
    /// The object receiving compatible property values.
    /// </param>
    /// <param name="snapshots">
    /// The previously captured property snapshots.
    /// </param>
    /// <param name="mode">
    /// The failure policy for matching but incompatible properties.
    /// </param>
    /// <param name="context">
    /// Optional immutable converter context.
    /// </param>
    /// <returns>
    /// A summary containing restored, ignored, and skipped properties.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when an argument is null.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="mode"/> is unknown.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when strict restoration or an object-level callback fails.
    /// </exception>
    public SerializationPropertyRestoreResult RestoreProperties(
        ISerializable target,
        IReadOnlyList<SerializationPropertySnapshot> snapshots,
        SerializationPropertyRestoreMode mode = SerializationPropertyRestoreMode.Strict,
        SerializationContext? context = null)
    {
        EnsureInitialized();
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(snapshots);
        if (!Enum.IsDefined(mode))
            throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown property restore mode.");
        using ConverterRegistryLease converters = m_converters.Capture();
        return PropertySnapshotPipeline.Restore(
            target,
            snapshots,
            mode,
            CreateContext(context),
            converters);
    }

    /// <summary>
    /// Restores one or more independently encoded persistent properties from neutral bytes.
    /// </summary>
    /// <param name="target">
    /// The existing object receiving the captured values.
    /// </param>
    /// <param name="data">
    /// Strict property snapshot bytes created by <see cref="CapturePropertyData"/> or <see cref="CapturePropertiesData"/>.
    /// </param>
    /// <param name="mode">
    /// The failure policy for matching but incompatible current properties.
    /// </param>
    /// <param name="context">
    /// Optional immutable converter context.
    /// </param>
    /// <returns>
    /// A summary containing restored, ignored, and skipped properties.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="target"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="mode"/> is unknown.
    /// </exception>
    /// <exception cref="InvalidDataException">
    /// Thrown when <paramref name="data"/> is malformed or unsupported.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when strict restoration or an object-level callback fails.
    /// </exception>
    public SerializationPropertyRestoreResult RestorePropertiesData(
        ISerializable target,
        ReadOnlySpan<byte> data,
        SerializationPropertyRestoreMode mode = SerializationPropertyRestoreMode.Strict,
        SerializationContext? context = null)
    {
        EnsureInitialized();
        ArgumentNullException.ThrowIfNull(target);
        if (!Enum.IsDefined(mode))
            throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown property restore mode.");
        using ConverterRegistryLease converters = m_converters.Capture();
        return PropertySnapshotPipeline.Restore(
            target,
            PropertySnapshotBinaryFormat.Decode(data),
            mode,
            CreateContext(context),
            converters);
    }

    /// <summary>
    /// Serializes a complete serializable root object into deterministic binary data.
    /// </summary>
    /// <typeparam name="T">
    /// The declared concrete root type.
    /// </typeparam>
    /// <param name="value">
    /// The root object to serialize.
    /// </param>
    /// <param name="context">
    /// Optional immutable converter context.
    /// </param>
    /// <returns>
    /// The encoded bytes.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="value"/> is null.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the manager is not initialized.
    /// </exception>
    public byte[] Serialize<T>(T value, SerializationContext? context = null)
        where T : class, ISerializable
    {
        EnsureInitialized();
        ArgumentNullException.ThrowIfNull(value);
        using ConverterRegistryLease converters = m_converters.Capture();
        return Serialize(value, converters, context);
    }

    internal byte[] Serialize<T>(
        T value,
        ConverterRegistryLease converters,
        SerializationContext? context = null)
        where T : class, ISerializable
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(converters);
        var operation = new SerializationOperation(CreateContext(context), converters);
        try
        {
            SerializationNode root = ValuePipeline.WriteRoot(value, typeof(T), operation);
            return BinarySerializationFormat.Encode(root);
        }
        finally
        {
            operation.Fail();
        }
    }

    /// <summary>
    /// Deserializes a new serializable root object from deterministic binary data.
    /// </summary>
    /// <typeparam name="T">
    /// The concrete root type to create.
    /// </typeparam>
    /// <param name="bytes">
    /// The encoded bytes.
    /// </param>
    /// <param name="context">
    /// Optional immutable converter context.
    /// </param>
    /// <returns>
    /// The restored object.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the manager is not initialized.
    /// </exception>
    public T Deserialize<T>(ReadOnlySpan<byte> bytes, SerializationContext? context = null)
        where T : class, ISerializable
    {
        EnsureInitialized();
        using ConverterRegistryLease converters = m_converters.Capture();
        return Deserialize<T>(bytes, converters, context);
    }

    internal T Deserialize<T>(
        ReadOnlySpan<byte> bytes,
        ConverterRegistryLease converters,
        SerializationContext? context = null)
        where T : class, ISerializable
    {
        ArgumentNullException.ThrowIfNull(converters);
        SerializationNode root = BinarySerializationFormat.Decode(bytes);
        var operation = new SerializationOperation(CreateContext(context), converters);
        try
        {
            T result = (T)ValuePipeline.ReadRoot(root, typeof(T), operation);
            operation.Complete();
            return result;
        }
        catch
        {
            operation.Fail();
            throw;
        }
    }

    /// <summary>
    /// Restores deterministic binary data into an existing serializable root object.
    /// </summary>
    /// <typeparam name="T">
    /// The declared target contract.
    /// </typeparam>
    /// <param name="target">
    /// The existing target object.
    /// </param>
    /// <param name="bytes">
    /// The encoded bytes.
    /// </param>
    /// <param name="context">
    /// Optional immutable converter context.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="target"/> is null.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the manager is not initialized.
    /// </exception>
    public void Restore<T>(T target, ReadOnlySpan<byte> bytes, SerializationContext? context = null)
        where T : class, ISerializable
    {
        EnsureInitialized();
        ArgumentNullException.ThrowIfNull(target);
        using ConverterRegistryLease converters = m_converters.Capture();
        SerializationNode root = BinarySerializationFormat.Decode(bytes);
        var operation = new SerializationOperation(CreateContext(context), converters);
        try
        {
            ValuePipeline.RestoreRoot(target, root, target.GetType(), operation);
            operation.Complete();
        }
        catch
        {
            operation.Fail();
            throw;
        }
    }

    /// <summary>
    /// Encodes a manually defined structured schema into deterministic binary data.
    /// </summary>
    /// <param name="write">
    /// The callback that writes the root object.
    /// </param>
    /// <param name="context">
    /// Optional immutable converter context.
    /// </param>
    /// <returns>
    /// The encoded bytes.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="write"/> is null.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the manager is not initialized.
    /// </exception>
    public byte[] Encode(Action<SerializationWriter> write, SerializationContext? context = null)
    {
        EnsureInitialized();
        ArgumentNullException.ThrowIfNull(write);
        using ConverterRegistryLease converters = m_converters.Capture();
        return Encode(write, converters, context);
    }

    internal byte[] Encode(
        Action<SerializationWriter> write,
        ConverterRegistryLease converters,
        SerializationContext? context = null)
    {
        ArgumentNullException.ThrowIfNull(write);
        ArgumentNullException.ThrowIfNull(converters);
        var operation = new SerializationOperation(CreateContext(context), converters);
        try
        {
            var root = new ObjectSerializationNode();
            write(new SerializationWriter(operation, root, "$", typeof(object)));
            return BinarySerializationFormat.Encode(root);
        }
        finally
        {
            operation.Fail();
        }
    }

    /// <summary>
    /// Decodes a manually defined structured schema from deterministic binary data.
    /// </summary>
    /// <typeparam name="TResult">
    /// The result produced by the read callback.
    /// </typeparam>
    /// <param name="bytes">
    /// The encoded bytes.
    /// </param>
    /// <param name="read">
    /// The callback that reads the root object.
    /// </param>
    /// <param name="context">
    /// Optional immutable converter context.
    /// </param>
    /// <returns>
    /// The callback result after all completion callbacks succeed.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="read"/> is null.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the manager is not initialized.
    /// </exception>
    public TResult Decode<TResult>(
        ReadOnlySpan<byte> bytes,
        Func<SerializationReader, TResult> read,
        SerializationContext? context = null)
    {
        EnsureInitialized();
        ArgumentNullException.ThrowIfNull(read);
        using ConverterRegistryLease converters = m_converters.Capture();
        return Decode(bytes, read, converters, context);
    }

    internal TResult Decode<TResult>(
        ReadOnlySpan<byte> bytes,
        Func<SerializationReader, TResult> read,
        ConverterRegistryLease converters,
        SerializationContext? context = null)
    {
        ArgumentNullException.ThrowIfNull(read);
        ArgumentNullException.ThrowIfNull(converters);
        SerializationNode decoded = BinarySerializationFormat.Decode(bytes);
        if (decoded is not ObjectSerializationNode root)
            throw new InvalidOperationException("The advanced serialization root must be an object.");

        var operation = new SerializationOperation(CreateContext(context), converters);
        try
        {
            TResult result = read(new SerializationReader(operation, root, "$", typeof(object)));
            operation.Complete();
            return result;
        }
        catch
        {
            operation.Fail();
            throw;
        }
    }

    private void EnsureInitialized()
    {
        if (m_disposed)
        {
            throw new ObjectDisposedException(nameof(SerializationRegistry));
        }
    }

    private SerializationContext CreateContext(SerializationContext? context)
        => (context ?? SerializationContext.empty).With(m_types).With(this);
}
