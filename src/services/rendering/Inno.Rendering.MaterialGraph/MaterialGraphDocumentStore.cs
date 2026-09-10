using System;
using Inno.Core.Graphs;
using Inno.Core.Serialization;
using Inno.Rendering;

namespace Inno.Rendering.MaterialGraph;

/// <summary>
/// Persists an authoring graph inside the same ordinary material asset that receives its evaluated values.
/// </summary>
public static class MaterialGraphDocumentStore
{
    /// <summary>
    /// Gets the stable material metadata key that owns the graph payload.
    /// </summary>
    public const string metadataKey = "inno.material-graph.document-v1";

    /// <summary>
    /// Creates an editable graph from persisted authoring data or the material's current values.
    /// </summary>
    /// <param name="material">
    /// Ordinary material that owns the embedded authoring document.
    /// </param>
    /// <param name="serialization">
    /// Serialization generation used by graph values.
    /// </param>
    /// <returns>
    /// A detached editable graph document.
    /// </returns>
    public static GraphDocument ReadOrCreate(
        MaterialAsset material,
        SerializationRegistry serialization)
    {
        ArgumentNullException.ThrowIfNull(material);
        ArgumentNullException.ThrowIfNull(serialization);
        if (!material.TryGetMetadata(metadataKey, out string? encoded)
            || string.IsNullOrWhiteSpace(encoded))
        {
            return MaterialGraphDocumentFactory.Create(material, serialization);
        }

        try
        {
            return GraphDocumentCodec.Decode(Convert.FromBase64String(encoded), serialization);
        }
        catch (Exception exception) when (exception is FormatException or InvalidOperationException)
        {
            throw new InvalidOperationException(
                $"Material '{material.assetPath}' contains an invalid embedded Material Graph document.",
                exception);
        }
    }

    /// <summary>
    /// Writes one isolated graph payload into the owning material.
    /// </summary>
    /// <param name="material">
    /// Ordinary material that owns the authoring document.
    /// </param>
    /// <param name="document">
    /// Detached graph document to persist.
    /// </param>
    /// <param name="serialization">
    /// Serialization generation used by graph values.
    /// </param>
    public static void Write(
        MaterialAsset material,
        GraphDocument document,
        SerializationRegistry serialization)
    {
        ArgumentNullException.ThrowIfNull(material);
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(serialization);
        material.SetMetadata(
            metadataKey,
            Convert.ToBase64String(GraphDocumentCodec.Encode(document.Clone(), serialization)));
    }
}
