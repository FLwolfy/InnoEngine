using System;
using Inno.Assets;
using Inno.Core.Graphs;
using Inno.Extensibility.Types;
using Inno.Core.Serialization;
using Inno.Scripting.Api;

namespace Inno.Rendering.ShaderGraph;

/// <summary>
/// Represents one native neutral shader graph without prescribing a rendering contract.
/// </summary>
[StableTypeId("2364a560-d890-4ec4-8ba7-5fa47404c140")]
public sealed class ShaderGraphAsset : ShaderAsset
{
    [SerializableProperty(PropertyVisibility.Hide)]
    private byte[] m_documentData = [];

    /// <summary>
    /// Gets the currently committed neutral graph document.
    /// </summary>
    public GraphDocument? document { get; private set; }

    /// <summary>
    /// Replaces the editable neutral graph document.
    /// </summary>
    /// <param name="document">
    /// Complete graph state to copy into this asset.
    /// </param>
    /// <param name="serialization">
    /// The serialization registry that owns the active graph converter generation.
    /// </param>
    [ScriptingApiIgnore]
    public void SetDocument(
        GraphDocument document,
        SerializationRegistry serialization)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(serialization);
        this.document = document.Clone();
        m_documentData = GraphDocumentCodec.Encode(this.document, serialization);
    }

    internal void CommitDefinition(
        ShaderDefinition definition,
        SerializationRegistry serialization)
        => SetDefinition(definition, serialization);

    [OnSerializableRestored]
    private void OnSerializableRestored(SerializationContext context)
    {
        document = m_documentData.Length == 0
            ? null
            : GraphDocumentCodec.Decode(
                m_documentData,
                context.GetRequired<SerializationRegistry>());
    }
}

/// <summary>
/// Contains one decoded shader graph source document.
/// </summary>
public sealed class ShaderGraphDocumentData
{
    /// <summary>
    /// Creates decoded shader graph data.
    /// </summary>
    /// <param name="document">
    /// Neutral graph document.
    /// </param>
    public ShaderGraphDocumentData(GraphDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        this.document = document;
    }

    /// <summary>
    /// Gets the neutral graph document.
    /// </summary>
    public GraphDocument document { get; }
}

/// <summary>
/// Provides the native editable shader graph document format.
/// </summary>
public static class ShaderGraphDocumentCodec
{
    /// <summary>
    /// Encodes a neutral shader graph through Inno serialization.
    /// </summary>
    /// <returns>
    /// Deterministic native document bytes.
    /// </returns>
    /// <param name="document">
    /// The document consumed by encode; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <param name="serialization">
    /// The serialization registry that owns the active graph converter generation.
    /// </param>
    public static byte[] Encode(
        GraphDocument document,
        SerializationRegistry serialization)
        => GraphDocumentCodec.Encode(document, serialization);

    /// <summary>
    /// Decodes a neutral shader graph from Inno serialization.
    /// </summary>
    /// <param name="bytes">
    /// Complete native document bytes.
    /// </param>
    /// <param name="serialization">
    /// The serialization registry that owns the active graph converter generation.
    /// </param>
    /// <returns>
    /// The detached decoded graph data.
    /// </returns>
    public static ShaderGraphDocumentData Decode(
        ReadOnlySpan<byte> bytes,
        SerializationRegistry serialization)
        => new(GraphDocumentCodec.Decode(bytes, serialization));
}
