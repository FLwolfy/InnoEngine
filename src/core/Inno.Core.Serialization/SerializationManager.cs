using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

using Inno.Core.Reflection;

namespace Inno.Core.Serialization;

/// <summary>
/// Manages serialization lifecycle and provides all root serialization operations.
/// </summary>
public static class SerializationManager
{
    private static readonly Lock S_LIFECYCLE_LOCK = new();

    /// <summary>
    /// Gets whether the serialization type catalog is initialized.
    /// </summary>
    public static bool isInitialized { get; private set; }

    /// <summary>
    /// Initializes serialization converters from the current <see cref="TypeCacheManager"/> catalog.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when <see cref="TypeCacheManager"/> has not been initialized.
    /// </exception>
    public static void Initialize()
    {
        lock (S_LIFECYCLE_LOCK)
        {
            if (!TypeCacheManager.isInitialized)
            {
                throw new InvalidOperationException(
                    "SerializationManager requires TypeCacheManager to be initialized first.");
            }

            ConverterRegistry.Initialize();
            isInitialized = true;
        }
    }

    /// <summary>
    /// Clears cached converter instances and marks serialization services as uninitialized.
    /// </summary>
    public static void Shutdown()
    {
        lock (S_LIFECYCLE_LOCK)
        {
            ConverterRegistry.Shutdown();
            isInitialized = false;
        }
    }

    /// <summary>
    /// Gets the stable ordered runtime-visible properties for a serializable object.
    /// </summary>
    /// <param name="value">The object whose property metadata should be created.</param>
    /// <returns>The properties that permit runtime reads.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="value"/> is null.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the manager is not initialized.</exception>
    public static IReadOnlyList<SerializedProperty> GetProperties(ISerializable value)
    {
        EnsureInitialized();
        ArgumentNullException.ThrowIfNull(value);
        return ReflectionMetadata.GetRuntimeProperties(value);
    }

    /// <summary>
    /// Captures each persistent property as an independent value for schema-aware migration.
    /// </summary>
    /// <param name="value">The object whose serializable properties should be captured.</param>
    /// <param name="context">Optional immutable converter context.</param>
    /// <returns>Ordered independent property snapshots.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="value"/> is null.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the manager is not initialized.</exception>
    public static IReadOnlyList<SerializationPropertySnapshot> CaptureProperties(
        ISerializable value,
        SerializationContext? context = null)
    {
        EnsureInitialized();
        ArgumentNullException.ThrowIfNull(value);
        return PropertySnapshotPipeline.Capture(value, context ?? SerializationContext.empty);
    }

    /// <summary>
    /// Captures one persistent property as independently restorable neutral bytes.
    /// </summary>
    /// <param name="value">The serializable object containing the requested property.</param>
    /// <param name="propertyName">The exact serialized member key to capture.</param>
    /// <param name="context">Optional immutable converter context.</param>
    /// <returns>Neutral bytes containing the property name and encoded value.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="value"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="propertyName"/> is empty or does not identify a persistent property.
    /// </exception>
    /// <exception cref="InvalidOperationException">Thrown when the manager is not initialized.</exception>
    public static byte[] CapturePropertyData(
        ISerializable value,
        string propertyName,
        SerializationContext? context = null)
    {
        EnsureInitialized();
        ArgumentNullException.ThrowIfNull(value);
        SerializationPropertySnapshot snapshot = PropertySnapshotPipeline.CaptureProperty(
            value,
            propertyName,
            context ?? SerializationContext.empty);
        return PropertySnapshotBinaryFormat.Encode([snapshot]);
    }

    /// <summary>
    /// Captures every persistent property as neutral bytes without serializing the owning object graph.
    /// </summary>
    /// <param name="value">The serializable object whose persistent properties should be captured.</param>
    /// <param name="context">Optional immutable converter context.</param>
    /// <returns>Versioned bytes containing independently encoded property values.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="value"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the manager is not initialized.</exception>
    public static byte[] CapturePropertiesData(
        ISerializable value,
        SerializationContext? context = null)
    {
        EnsureInitialized();
        ArgumentNullException.ThrowIfNull(value);
        return PropertySnapshotBinaryFormat.Encode(
            PropertySnapshotPipeline.Capture(value, context ?? SerializationContext.empty));
    }

    /// <summary>
    /// Restores independently captured properties into an existing object.
    /// </summary>
    /// <param name="target">The object receiving compatible property values.</param>
    /// <param name="snapshots">The previously captured property snapshots.</param>
    /// <param name="mode">The failure policy for matching but incompatible properties.</param>
    /// <param name="context">Optional immutable converter context.</param>
    /// <returns>A summary containing restored, ignored, and skipped properties.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="mode"/> is unknown.</exception>
    /// <exception cref="InvalidOperationException">Thrown when strict restoration or an object-level callback fails.</exception>
    public static SerializationPropertyRestoreResult RestoreProperties(
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
        return PropertySnapshotPipeline.Restore(
            target,
            snapshots,
            mode,
            context ?? SerializationContext.empty);
    }

    /// <summary>
    /// Restores one or more independently encoded persistent properties from neutral bytes.
    /// </summary>
    /// <param name="target">The existing object receiving the captured values.</param>
    /// <param name="data">Versioned bytes created by <see cref="CapturePropertyData"/> or <see cref="CapturePropertiesData"/>.</param>
    /// <param name="mode">The failure policy for matching but incompatible current properties.</param>
    /// <param name="context">Optional immutable converter context.</param>
    /// <returns>A summary containing restored, ignored, and skipped properties.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="target"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="mode"/> is unknown.</exception>
    /// <exception cref="InvalidDataException">Thrown when <paramref name="data"/> is malformed or unsupported.</exception>
    /// <exception cref="InvalidOperationException">Thrown when strict restoration or an object-level callback fails.</exception>
    public static SerializationPropertyRestoreResult RestorePropertiesData(
        ISerializable target,
        ReadOnlySpan<byte> data,
        SerializationPropertyRestoreMode mode = SerializationPropertyRestoreMode.Strict,
        SerializationContext? context = null)
    {
        EnsureInitialized();
        ArgumentNullException.ThrowIfNull(target);
        if (!Enum.IsDefined(mode))
            throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown property restore mode.");
        return PropertySnapshotPipeline.Restore(
            target,
            PropertySnapshotBinaryFormat.Decode(data),
            mode,
            context ?? SerializationContext.empty);
    }

    /// <summary>
    /// Serializes a complete serializable root object into deterministic binary data.
    /// </summary>
    /// <typeparam name="T">The declared concrete root type.</typeparam>
    /// <param name="value">The root object to serialize.</param>
    /// <param name="context">Optional immutable converter context.</param>
    /// <returns>The encoded bytes.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="value"/> is null.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the manager is not initialized.</exception>
    public static byte[] Serialize<T>(T value, SerializationContext? context = null)
        where T : class, ISerializable
    {
        EnsureInitialized();
        ArgumentNullException.ThrowIfNull(value);
        var operation = new SerializationOperation(context ?? SerializationContext.empty);
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
    /// <typeparam name="T">The concrete root type to create.</typeparam>
    /// <param name="bytes">The encoded bytes.</param>
    /// <param name="context">Optional immutable converter context.</param>
    /// <returns>The restored object.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the manager is not initialized.</exception>
    public static T Deserialize<T>(ReadOnlySpan<byte> bytes, SerializationContext? context = null)
        where T : class, ISerializable
    {
        EnsureInitialized();
        SerializationNode root = BinarySerializationFormat.Decode(bytes);
        var operation = new SerializationOperation(context ?? SerializationContext.empty);
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
    /// <typeparam name="T">The declared target contract.</typeparam>
    /// <param name="target">The existing target object.</param>
    /// <param name="bytes">The encoded bytes.</param>
    /// <param name="context">Optional immutable converter context.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="target"/> is null.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the manager is not initialized.</exception>
    public static void Restore<T>(T target, ReadOnlySpan<byte> bytes, SerializationContext? context = null)
        where T : class, ISerializable
    {
        EnsureInitialized();
        ArgumentNullException.ThrowIfNull(target);
        SerializationNode root = BinarySerializationFormat.Decode(bytes);
        var operation = new SerializationOperation(context ?? SerializationContext.empty);
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
    /// <param name="write">The callback that writes the root object.</param>
    /// <param name="context">Optional immutable converter context.</param>
    /// <returns>The encoded bytes.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="write"/> is null.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the manager is not initialized.</exception>
    public static byte[] Encode(Action<SerializationWriter> write, SerializationContext? context = null)
    {
        EnsureInitialized();
        ArgumentNullException.ThrowIfNull(write);
        var operation = new SerializationOperation(context ?? SerializationContext.empty);
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
    /// <typeparam name="TResult">The result produced by the read callback.</typeparam>
    /// <param name="bytes">The encoded bytes.</param>
    /// <param name="read">The callback that reads the root object.</param>
    /// <param name="context">Optional immutable converter context.</param>
    /// <returns>The callback result after all completion callbacks succeed.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="read"/> is null.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the manager is not initialized.</exception>
    public static TResult Decode<TResult>(
        ReadOnlySpan<byte> bytes,
        Func<SerializationReader, TResult> read,
        SerializationContext? context = null)
    {
        EnsureInitialized();
        ArgumentNullException.ThrowIfNull(read);
        SerializationNode decoded = BinarySerializationFormat.Decode(bytes);
        if (decoded is not ObjectSerializationNode root)
            throw new InvalidOperationException("The advanced serialization root must be an object.");

        var operation = new SerializationOperation(context ?? SerializationContext.empty);
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

    private static void EnsureInitialized()
    {
        if (!isInitialized)
        {
            throw new InvalidOperationException(
                "SerializationManager is not initialized. Initialize AssemblyManager, TypeCacheManager, and SerializationManager before using serialization APIs.");
        }
    }
}
