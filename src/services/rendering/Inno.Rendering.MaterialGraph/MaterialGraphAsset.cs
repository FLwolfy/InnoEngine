using System;
using Inno.Core.Graphs;
using Inno.Core.Serialization;
using Inno.Extensibility.Types;
using Inno.Rendering;
using Inno.Scripting.Api;

namespace Inno.Rendering.MaterialGraph;

/// <summary>
/// Represents a material whose persistent values are authored as a neutral node graph.
/// </summary>
[StableTypeId("6205722d-59ce-4a96-a375-aed9863246fb")]
public sealed class MaterialGraphAsset : MaterialAsset
{
    [SerializableProperty(PropertyVisibility.Hide)]
    private byte[] m_documentData = [];

    /// <summary>
    /// Gets the currently committed neutral material-mapping document.
    /// </summary>
    [ScriptingApiIgnore]
    public GraphDocument? document { get; private set; }

    /// <summary>
    /// Replaces the editable graph document with an isolated copy.
    /// </summary>
    /// <param name="document">
    /// Complete graph state to commit.
    /// </param>
    /// <param name="serialization">
    /// Serialization generation used for graph values.
    /// </param>
    [ScriptingApiIgnore]
    public void SetDocument(GraphDocument document, SerializationRegistry serialization)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(serialization);
        this.document = document.Clone();
        m_documentData = GraphDocumentCodec.Encode(this.document, serialization);
    }

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
/// Contains one decoded material graph source document.
/// </summary>
public sealed class MaterialGraphDocumentData
{
    /// <summary>
    /// Creates decoded material graph data.
    /// </summary>
    /// <param name="document">
    /// Detached neutral graph document.
    /// </param>
    public MaterialGraphDocumentData(GraphDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        this.document = document;
    }

    /// <summary>
    /// Gets the detached neutral graph document.
    /// </summary>
    public GraphDocument document { get; }
}

/// <summary>
/// Encodes and decodes native material graph documents through the shared graph codec.
/// </summary>
public static class MaterialGraphDocumentCodec
{
    /// <summary>
    /// Encodes a material graph into deterministic native bytes.
    /// </summary>
    /// <param name="document">
    /// Graph document to encode.
    /// </param>
    /// <param name="serialization">
    /// Serialization generation used by graph values.
    /// </param>
    /// <returns>
    /// Detached native document bytes.
    /// </returns>
    public static byte[] Encode(GraphDocument document, SerializationRegistry serialization)
        => GraphDocumentCodec.Encode(document, serialization);

    /// <summary>
    /// Decodes a material graph from native bytes.
    /// </summary>
    /// <param name="bytes">
    /// Complete native graph payload.
    /// </param>
    /// <param name="serialization">
    /// Serialization generation used by graph values.
    /// </param>
    /// <returns>
    /// The detached decoded graph data.
    /// </returns>
    public static MaterialGraphDocumentData Decode(
        ReadOnlySpan<byte> bytes,
        SerializationRegistry serialization)
        => new(GraphDocumentCodec.Decode(bytes, serialization));
}
