using System;
using System.Buffers.Binary;

using Inno.Assets;
using Inno.Extensibility.Types;

namespace Inno.Text;

/// <summary>
/// Describes immutable metadata for an imported OpenType font source.
/// </summary>
public readonly record struct FontMetadata
{
    /// <summary>
    /// Creates validated imported font metadata.
    /// </summary>
    /// <param name="faceCount">
    /// The number of faces in the source collection.
    /// </param>
    /// <param name="encodedByteLength">
    /// The encoded source length in bytes.
    /// </param>
    public FontMetadata(int faceCount, long encodedByteLength)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(faceCount);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(encodedByteLength);
        this.faceCount = faceCount;
        this.encodedByteLength = encodedByteLength;
    }

    /// <summary>
    /// Gets the number of independently addressable faces in the source.
    /// </summary>
    public int faceCount { get; }

    /// <summary>
    /// Gets the encoded source length in bytes.
    /// </summary>
    public long encodedByteLength { get; }
}

/// <summary>
/// Encodes the compact runtime payload shared by the font importer and text runtime.
/// </summary>
public static class FontMetadataCodec
{
    private const uint C_MAGIC = 0x544E4649;
    private const int C_SIZE = 16;

    /// <summary>
    /// Encodes validated metadata into a deterministic runtime payload.
    /// </summary>
    /// <param name="metadata">
    /// The metadata to encode.
    /// </param>
    /// <returns>
    /// The compact runtime payload.
    /// </returns>
    public static byte[] Encode(FontMetadata metadata)
    {
        byte[] output = new byte[C_SIZE];
        BinaryPrimitives.WriteUInt32LittleEndian(output, C_MAGIC);
        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(4), metadata.faceCount);
        BinaryPrimitives.WriteInt64LittleEndian(output.AsSpan(8), metadata.encodedByteLength);
        return output;
    }

    /// <summary>
    /// Decodes and validates a compact font runtime payload.
    /// </summary>
    /// <param name="payload">
    /// The complete runtime payload.
    /// </param>
    /// <returns>
    /// The decoded metadata.
    /// </returns>
    public static FontMetadata Decode(ReadOnlySpan<byte> payload)
    {
        if (payload.Length != C_SIZE || BinaryPrimitives.ReadUInt32LittleEndian(payload) != C_MAGIC)
            throw new InvalidOperationException("The font runtime payload is invalid.");
        try
        {
            return new FontMetadata(
                BinaryPrimitives.ReadInt32LittleEndian(payload[4..]),
                BinaryPrimitives.ReadInt64LittleEndian(payload[8..]));
        }
        catch (ArgumentOutOfRangeException exception)
        {
            throw new InvalidOperationException("The font runtime payload contains invalid metadata.", exception);
        }
    }
}

/// <summary>
/// Represents one imported OpenType font file or collection.
/// </summary>
[StableTypeId("c8c708e2-3fd1-4ee1-9f16-5a47680272af")]
public sealed class FontAsset : AssetObject
{
    private FontMetadata? m_metadata;

    /// <summary>
    /// Gets imported metadata, or null before runtime content is loaded.
    /// </summary>
    public FontMetadata? metadata => m_metadata;

    /// <summary>
    /// Refreshes imported metadata after an artifact commit.
    /// </summary>
    /// <param name="previousPayload">
    /// The previous compact runtime payload.
    /// </param>
    /// <param name="currentPayload">
    /// The current compact runtime payload.
    /// </param>
    protected override void OnRuntimePayloadChanged(
        ReadOnlyMemory<byte> previousPayload,
        ReadOnlyMemory<byte> currentPayload)
    {
        m_metadata = currentPayload.IsEmpty ? null : FontMetadataCodec.Decode(currentPayload.Span);
    }
}
