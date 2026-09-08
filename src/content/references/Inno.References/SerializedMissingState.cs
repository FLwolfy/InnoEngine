using System;

namespace Inno.References;

/// <summary>
/// Preserves one recoverable slot and its neutral owner state without retaining runtime objects.
/// </summary>
public sealed class SerializedMissingState
{
    private readonly byte[] m_payload;

    /// <summary>
    /// Creates an immutable missing-state record.
    /// </summary>
    /// <param name="key">
    /// The persistent owner slot represented by this record.
    /// </param>
    /// <param name="descriptor">
    /// The persistent target intent that must survive unavailability.
    /// </param>
    /// <param name="payload">
    /// The neutral serialized owner state required for reconstruction.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="descriptor"/> is null.
    /// </exception>
    public SerializedMissingState(ReferenceKey key, ReferenceDescriptor descriptor, ReadOnlySpan<byte> payload)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        this.key = key;
        this.descriptor = descriptor;
        m_payload = payload.ToArray();
    }

    /// <summary>
    /// Gets the persistent owner slot represented by this record.
    /// </summary>
    public ReferenceKey key { get; }

    /// <summary>
    /// Gets the persistent target intent preserved by this record.
    /// </summary>
    public ReferenceDescriptor descriptor { get; }

    /// <summary>
    /// Gets immutable neutral bytes used to reconstruct the owner state.
    /// </summary>
    public ReadOnlyMemory<byte> payload => m_payload;
}
