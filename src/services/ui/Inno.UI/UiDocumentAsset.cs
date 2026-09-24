using System;
using System.IO;
using System.Text;

using Inno.Assets;
using Inno.Extensibility.Types;

namespace Inno.UI;

/// <summary>Represents one imported, language-tagged UI document source.</summary>
[StableTypeId("8b34bafb-f34a-4c19-9853-282485c40b78")]
public sealed class UiDocumentAsset : AssetObject
{
    private const uint C_PAYLOAD_MAGIC = 0x44495549;
    private UiDocumentSource? m_source;
    private string m_implementationId = string.Empty;

    /// <summary>Gets the frozen source, or null before runtime content is loaded.</summary>
    public UiDocumentSource? source => m_source;

    /// <summary>Gets the exact backend implementation selected when the source was imported.</summary>
    public string implementationId => m_implementationId;

    /// <summary>Encodes an importer-owned runtime payload without exposing an implementation-specific format.</summary>
    /// <param name="implementationId">Stable backend implementation identity.</param>
    /// <param name="source">Validated frozen document source.</param>
    /// <returns>Complete deterministic runtime payload bytes.</returns>
    public static byte[] CreateRuntimePayload(string implementationId, UiDocumentSource source)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(implementationId);
        ArgumentNullException.ThrowIfNull(source);
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, new UTF8Encoding(false, true), leaveOpen: true))
        {
            writer.Write(C_PAYLOAD_MAGIC);
            writer.Write(implementationId);
            writer.Write(source.language.value);
            writer.Write(source.sourceUri);
            writer.Write(source.text);
        }
        return stream.ToArray();
    }

    /// <inheritdoc />
    protected override void OnRuntimePayloadChanged(
        ReadOnlyMemory<byte> previousPayload,
        ReadOnlyMemory<byte> currentPayload)
    {
        if (currentPayload.IsEmpty)
        {
            m_source = null;
            m_implementationId = string.Empty;
            return;
        }
        using var stream = new MemoryStream(currentPayload.ToArray(), writable: false);
        using var reader = new BinaryReader(stream, new UTF8Encoding(false, true), leaveOpen: false);
        if (reader.ReadUInt32() != C_PAYLOAD_MAGIC)
            throw new InvalidDataException("The UI document runtime payload has an invalid header.");
        string implementation = reader.ReadString();
        string language = reader.ReadString();
        string sourceUri = reader.ReadString();
        string text = reader.ReadString();
        if (stream.Position != stream.Length)
            throw new InvalidDataException("The UI document runtime payload contains trailing data.");
        ArgumentException.ThrowIfNullOrWhiteSpace(implementation);
        m_implementationId = implementation;
        m_source = new UiDocumentSource(new UiDocumentLanguageId(language), text, sourceUri);
    }
}
