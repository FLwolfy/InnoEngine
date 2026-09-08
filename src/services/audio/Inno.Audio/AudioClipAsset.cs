using System;
using System.Buffers.Binary;
using System.Text;
using Inno.Assets;
using Inno.Extensibility.Types;

namespace Inno.Audio;

/// <summary>
/// Describes imported encoded audio independently from its runtime storage path.
/// </summary>
public readonly record struct AudioClipMetadata
{
    /// <summary>
    /// Creates imported audio metadata.
    /// </summary>
    /// <param name="codec">
    /// Codec protocol of the encoded artifact.
    /// </param>
    /// <param name="channels">
    /// Encoded channel count.
    /// </param>
    /// <param name="sampleRate">
    /// Sample rate in frames per second.
    /// </param>
    /// <param name="frameCount">
    /// Total decoded frame count.
    /// </param>
    /// <param name="encodedByteLength">
    /// Encoded artifact length in bytes.
    /// </param>
    public AudioClipMetadata(
        AudioCodecId codec,
        int channels,
        int sampleRate,
        long frameCount,
        long encodedByteLength)
    {
        if (!codec.isValid)
            throw new ArgumentException("A valid codec identifier is required.", nameof(codec));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(channels);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
        ArgumentOutOfRangeException.ThrowIfNegative(frameCount);
        ArgumentOutOfRangeException.ThrowIfNegative(encodedByteLength);
        this.codec = codec;
        this.channels = channels;
        this.sampleRate = sampleRate;
        this.frameCount = frameCount;
        this.encodedByteLength = encodedByteLength;
    }

    /// <summary>
    /// Gets the codec protocol of the encoded artifact.
    /// </summary>
    public AudioCodecId codec { get; }

    /// <summary>
    /// Gets the encoded channel count.
    /// </summary>
    public int channels { get; }

    /// <summary>
    /// Gets the sample rate in frames per second.
    /// </summary>
    public int sampleRate { get; }

    /// <summary>
    /// Gets the total decoded frame count.
    /// </summary>
    public long frameCount { get; }

    /// <summary>
    /// Gets the encoded artifact length in bytes.
    /// </summary>
    public long encodedByteLength { get; }

    /// <summary>
    /// Gets the clip duration derived from frame count and sample rate.
    /// </summary>
    public TimeSpan duration => TimeSpan.FromSeconds((double)frameCount / sampleRate);
}

/// <summary>
/// Encodes the compact runtime header shared by audio importers and runtime asset loading.
/// </summary>
public static class AudioClipMetadataCodec
{
    private const uint C_MAGIC = 0x44554149;
    private const int C_FIXED_SIZE = 32;

    /// <summary>
    /// Encodes audio metadata into a compact deterministic runtime header.
    /// </summary>
    /// <param name="metadata">
    /// Valid metadata to encode.
    /// </param>
    /// <returns>
    /// Newly allocated runtime header bytes.
    /// </returns>
    public static byte[] Encode(AudioClipMetadata metadata)
    {
        byte[] codec = Encoding.UTF8.GetBytes(metadata.codec.value);
        if (codec.Length > byte.MaxValue)
            throw new ArgumentException("The codec identifier is too long.", nameof(metadata));
        byte[] output = new byte[C_FIXED_SIZE + codec.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(0, 4), C_MAGIC);
        output[4] = (byte)codec.Length;
        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(8, 4), metadata.channels);
        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(12, 4), metadata.sampleRate);
        BinaryPrimitives.WriteInt64LittleEndian(output.AsSpan(16, 8), metadata.frameCount);
        BinaryPrimitives.WriteInt64LittleEndian(output.AsSpan(24, 8), metadata.encodedByteLength);
        codec.CopyTo(output.AsSpan(C_FIXED_SIZE));
        return output;
    }

    /// <summary>
    /// Decodes and validates one compact audio runtime header.
    /// </summary>
    /// <param name="payload">
    /// Complete runtime header bytes.
    /// </param>
    /// <returns>
    /// Valid imported audio metadata.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the header is truncated, corrupt, or semantically invalid.
    /// </exception>
    public static AudioClipMetadata Decode(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < C_FIXED_SIZE || BinaryPrimitives.ReadUInt32LittleEndian(payload) != C_MAGIC)
            throw new InvalidOperationException("The audio runtime header is invalid.");
        int codecLength = payload[4];
        if (payload.Length != C_FIXED_SIZE + codecLength || codecLength == 0)
            throw new InvalidOperationException("The audio runtime header length is invalid.");
        try
        {
            return new AudioClipMetadata(
                new AudioCodecId(Encoding.UTF8.GetString(payload[C_FIXED_SIZE..])),
                BinaryPrimitives.ReadInt32LittleEndian(payload[8..12]),
                BinaryPrimitives.ReadInt32LittleEndian(payload[12..16]),
                BinaryPrimitives.ReadInt64LittleEndian(payload[16..24]),
                BinaryPrimitives.ReadInt64LittleEndian(payload[24..32]));
        }
        catch (ArgumentException exception)
        {
            throw new InvalidOperationException("The audio runtime header contains invalid metadata.", exception);
        }
    }
}

/// <summary>
/// Represents one imported audio source and its compact runtime metadata.
/// </summary>
[StableTypeId("c64bbcb2-f7ee-48f2-85df-4506dc7b86d5")]
public sealed class AudioClipAsset : AssetObject
{
    private AudioClipMetadata? m_metadata;

    /// <summary>
    /// Gets imported metadata, or <see langword="null"/> before runtime content is loaded.
    /// </summary>
    public AudioClipMetadata? metadata => m_metadata;

    /// <summary>
    /// Gets the imported clip duration, or zero before runtime content is loaded.
    /// </summary>
    public TimeSpan duration => m_metadata?.duration ?? TimeSpan.Zero;

    /// <summary>
    /// Refreshes imported metadata after an artifact commit.
    /// </summary>
    /// <param name="previousPayload">
    /// Previously committed compact header.
    /// </param>
    /// <param name="currentPayload">
    /// Newly committed compact header.
    /// </param>
    protected override void OnRuntimePayloadChanged(
        ReadOnlyMemory<byte> previousPayload,
        ReadOnlyMemory<byte> currentPayload)
    {
        m_metadata = currentPayload.IsEmpty
            ? null
            : AudioClipMetadataCodec.Decode(currentPayload.Span);
    }
}
