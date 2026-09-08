using System;
using System.Threading;

namespace Inno.Core.Serialization;

/// <summary>
/// Pins one immutable converter generation so serialization remains deterministic across
/// asynchronous continuations and extension reloads.
/// </summary>
/// <remarks>
/// A generation may execute operations from any thread after it has been captured. The owner must
/// dispose it when the surrounding transaction completes so collectible extension generations can unload.
/// </remarks>
public sealed class SerializationGeneration : IDisposable
{
    private readonly SerializationRegistry m_owner;
    private ConverterRegistryLease? m_converters;

    internal SerializationGeneration(
        SerializationRegistry owner,
        ConverterRegistryLease converters)
    {
        m_owner = owner;
        m_converters = converters;
    }

    /// <summary>
    /// Serializes a complete root object using the converter generation captured by this instance.
    /// </summary>
    /// <typeparam name="T">
    /// The declared concrete root type.
    /// </typeparam>
    /// <param name="value">
    /// The non-null root object to encode.
    /// </param>
    /// <param name="context">
    /// Optional immutable converter context for this operation.
    /// </param>
    /// <returns>
    /// Deterministic binary data encoded by the captured converter generation.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="value"/> is null.
    /// </exception>
    /// <exception cref="ObjectDisposedException">
    /// Thrown when this generation has already been disposed.
    /// </exception>
    public byte[] Serialize<T>(T value, SerializationContext? context = null)
        where T : class, ISerializable
        => m_owner.Serialize(value, GetConverters(), context);

    /// <summary>
    /// Deserializes a complete root object using the converter generation captured by this instance.
    /// </summary>
    /// <typeparam name="T">
    /// The concrete root type to create.
    /// </typeparam>
    /// <param name="bytes">
    /// Deterministic binary data containing the root object.
    /// </param>
    /// <param name="context">
    /// Optional immutable converter context for this operation.
    /// </param>
    /// <returns>
    /// The restored root object.
    /// </returns>
    /// <exception cref="ObjectDisposedException">
    /// Thrown when this generation has already been disposed.
    /// </exception>
    public T Deserialize<T>(ReadOnlySpan<byte> bytes, SerializationContext? context = null)
        where T : class, ISerializable
        => m_owner.Deserialize<T>(bytes, GetConverters(), context);

    /// <summary>
    /// Encodes a manually defined structured root using the converter generation captured by this instance.
    /// </summary>
    /// <param name="write">
    /// The callback that writes the complete root object.
    /// </param>
    /// <param name="context">
    /// Optional immutable converter context for this operation.
    /// </param>
    /// <returns>
    /// Deterministic binary data containing the structured root.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="write"/> is null.
    /// </exception>
    /// <exception cref="ObjectDisposedException">
    /// Thrown when this generation has already been disposed.
    /// </exception>
    public byte[] Encode(
        Action<SerializationWriter> write,
        SerializationContext? context = null)
        => m_owner.Encode(write, GetConverters(), context);

    /// <summary>
    /// Decodes a manually defined structured root using the converter generation captured by this instance.
    /// </summary>
    /// <typeparam name="TResult">
    /// The result produced by the read callback.
    /// </typeparam>
    /// <param name="bytes">
    /// Deterministic binary data containing the structured root.
    /// </param>
    /// <param name="read">
    /// The callback that consumes the complete root object.
    /// </param>
    /// <param name="context">
    /// Optional immutable converter context for this operation.
    /// </param>
    /// <returns>
    /// The callback result after all restoration callbacks complete.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="read"/> is null.
    /// </exception>
    /// <exception cref="ObjectDisposedException">
    /// Thrown when this generation has already been disposed.
    /// </exception>
    public TResult Decode<TResult>(
        ReadOnlySpan<byte> bytes,
        Func<SerializationReader, TResult> read,
        SerializationContext? context = null)
        => m_owner.Decode(bytes, read, GetConverters(), context);

    /// <summary>
    /// Releases the pinned converter generation and every collectible type reference owned by this lease.
    /// </summary>
    public void Dispose()
    {
        ConverterRegistryLease? converters = Interlocked.Exchange(ref m_converters, null);
        converters?.Dispose();
    }

    private ConverterRegistryLease GetConverters()
        => m_converters ?? throw new ObjectDisposedException(nameof(SerializationGeneration));
}
