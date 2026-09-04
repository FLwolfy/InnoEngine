using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;
using Inno.Core.Serialization;

namespace Inno.Runtime;

/// <summary>
/// Frames the serialized runtime manifest with the application identity required before engine startup.
/// </summary>
public static class RuntimeManifestEnvelope
{
    private static ReadOnlySpan<byte> magic => "INNORTM\0"u8;

    /// <summary>
    /// Encodes a validated runtime manifest into the strict deployment envelope.
    /// </summary>
    /// <param name="manifest">
    /// The manifest whose application identity and serialized payload must agree.
    /// </param>
    /// <param name="serialization">
    /// The immutable converter generation captured for the surrounding build transaction.
    /// </param>
    /// <returns>
    /// The complete deterministic deployment envelope.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="manifest"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="InvalidDataException">
    /// Thrown when the manifest is not valid for Player startup.
    /// </exception>
    public static byte[] Encode(
        GameRuntimeManifest manifest,
        SerializationGeneration serialization)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(serialization);
        manifest.Validate();
        byte[] applicationId = Encoding.UTF8.GetBytes(manifest.applicationId);
        byte[] payload = serialization.Serialize(manifest);
        byte[] result = new byte[checked(magic.Length + sizeof(int) + applicationId.Length + sizeof(int) + payload.Length)];
        int offset = 0;
        magic.CopyTo(result);
        offset += magic.Length;
        BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(offset), applicationId.Length);
        offset += sizeof(int);
        applicationId.CopyTo(result, offset);
        offset += applicationId.Length;
        BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(offset), payload.Length);
        offset += sizeof(int);
        payload.CopyTo(result, offset);
        return result;
    }

    /// <summary>
    /// Reads and validates the application identity without requiring serialization services to be initialized.
    /// </summary>
    /// <param name="data">
    /// The complete runtime manifest envelope.
    /// </param>
    /// <returns>
    /// The stable application identity used to select persistent storage.
    /// </returns>
    /// <exception cref="InvalidDataException">
    /// Thrown when the envelope is truncated, malformed, or contains an invalid application identity.
    /// </exception>
    public static string ReadApplicationId(ReadOnlySpan<byte> data)
    {
        Parse(data, out string applicationId, out _);
        ValidateApplicationId(applicationId);
        return applicationId;
    }

    /// <summary>
    /// Deserializes and validates the complete runtime manifest after engine serialization is available.
    /// </summary>
    /// <param name="data">
    /// The complete runtime manifest envelope.
    /// </param>
    /// <param name="serialization">
    /// The immutable converter generation selected for Player startup.
    /// </param>
    /// <returns>
    /// The validated runtime manifest.
    /// </returns>
    /// <exception cref="InvalidDataException">
    /// Thrown when the envelope or manifest is malformed, or when the framed identity disagrees with the payload.
    /// </exception>
    public static GameRuntimeManifest Decode(
        ReadOnlySpan<byte> data,
        SerializationGeneration serialization)
    {
        ArgumentNullException.ThrowIfNull(serialization);
        Parse(data, out string applicationId, out ReadOnlySpan<byte> payload);
        ValidateApplicationId(applicationId);
        GameRuntimeManifest manifest = serialization.Deserialize<GameRuntimeManifest>(payload);
        manifest.Validate();
        if (!string.Equals(applicationId, manifest.applicationId, StringComparison.Ordinal))
            throw new InvalidDataException("Runtime manifest application identities do not match.");
        return manifest;
    }

    private static void Parse(
        ReadOnlySpan<byte> data,
        out string applicationId,
        out ReadOnlySpan<byte> payload)
    {
        int minimumLength = magic.Length + sizeof(int) * 2;
        if (data.Length < minimumLength || !data[..magic.Length].SequenceEqual(magic))
            throw new InvalidDataException("Runtime manifest envelope has an invalid header.");
        int offset = magic.Length;
        int applicationIdLength = BinaryPrimitives.ReadInt32LittleEndian(data[offset..]);
        offset += sizeof(int);
        if (applicationIdLength <= 0 || applicationIdLength > 255 || applicationIdLength > data.Length - offset - sizeof(int))
            throw new InvalidDataException("Runtime manifest envelope has an invalid application identity length.");
        try
        {
            applicationId = new UTF8Encoding(false, true).GetString(data.Slice(offset, applicationIdLength));
        }
        catch (DecoderFallbackException exception)
        {
            throw new InvalidDataException("Runtime manifest application identity is not valid UTF-8.", exception);
        }
        offset += applicationIdLength;
        int payloadLength = BinaryPrimitives.ReadInt32LittleEndian(data[offset..]);
        offset += sizeof(int);
        if (payloadLength <= 0 || payloadLength != data.Length - offset)
            throw new InvalidDataException("Runtime manifest envelope has an invalid payload length.");
        payload = data[offset..];
    }

    private static void ValidateApplicationId(string applicationId)
    {
        if (string.IsNullOrWhiteSpace(applicationId))
            throw new InvalidDataException("Runtime manifest requires an application identity.");
        foreach (char character in applicationId)
        {
            if (!(character is >= 'a' and <= 'z'
                  || character is >= '0' and <= '9'
                  || character is '.' or '_' or '-'))
            {
                throw new InvalidDataException("Runtime manifest contains an invalid application identity.");
            }
        }
    }
}
