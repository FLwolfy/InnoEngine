using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Inno.Assets;
using Inno.Assets.Pipeline;
using Inno.Core.Mathematics;
using Inno.Rendering;
using Inno.Rendering.Assets;

namespace Inno.Rendering.Assets;

[AssetImporterExtension]
internal sealed class GeometryAssetImporter : AssetImporter<GeometryAsset>
{
    /// <summary>
    /// Gets the stable importer identity used in artifact fingerprints.
    /// </summary>
    public override string importerId => "inno.rendering.geometry";

    /// <summary>
    /// Gets the normalized source extensions accepted by this importer.
    /// </summary>
    public override IReadOnlyList<string> supportedExtensions { get; } = [".obj", ".gltf", ".glb"];

    /// <summary>
    /// Imports source content into a validated runtime asset and artifact set.
    /// </summary>
    /// <param name="context">
    /// The operation scope that provides state, services, and ownership boundaries.
    /// </param>
    /// <param name="output">
    /// The import output writer that receives runtime data and dependency declarations.
    /// </param>
    /// <param name="cancellationToken">
    /// The token that cancels the operation before it commits.
    /// </param>
    /// <returns>
    /// An asynchronous operation that completes after all requested work has finished.
    /// </returns>
    protected override async ValueTask ImportAsync(
        AssetImportContext context,
        AssetImportWriter<GeometryAsset> output,
        CancellationToken cancellationToken)
    {
        byte[] ReadDependency(string relativePath)
        {
            AssetPath dependency = AssetPath.Parse(relativePath);
            return context.ReadSourceBytes(dependency).ToArray();
        }

        GeometryData data = context.extension switch
        {
            ".obj" => MeshSourceParser.ParseObj(context.assetPath.ToString(), context.ReadUtf8Text()),
            ".gltf" => MeshSourceParser.ParseGltf(
                context.assetPath.ToString(),
                context.sourceBytes.Span,
                isBinary: false,
                ReadDependency,
                path => output.DependsOnSource(AssetPath.Parse(path))),
            ".glb" => MeshSourceParser.ParseGltf(
                context.assetPath.ToString(),
                context.sourceBytes.Span,
                isBinary: true,
                ReadDependency,
                path => output.DependsOnSource(AssetPath.Parse(path))),
            _ => throw new RenderingAssetFormatException(context.assetPath.ToString(), "Unsupported geometry container.")
        };
        (Vector3 boundsCenter, Vector3 boundsExtents) = CalculateBounds(data);
        var asset = new GeometryAsset(
            data.vertices.Count,
            data.indices.Count,
            data.sections.Count,
            boundsCenter,
            boundsExtents);
        output.SetAsset(asset);
        byte[] artifact = GeometryArtifact.Encode(data);
        await output.WriteArtifactAsync("runtime", artifact, cancellationToken).ConfigureAwait(false);
        await output.WriteArtifactAsync("geometry", artifact, cancellationToken).ConfigureAwait(false);
    }

    private static (Vector3 center, Vector3 extents) CalculateBounds(GeometryData data)
    {
        if (data.vertices.Count == 0)
        {
            return (Vector3.ZERO, Vector3.ZERO);
        }

        Vector3 minimum = data.vertices[0].position;
        Vector3 maximum = minimum;
        for (int index = 1; index < data.vertices.Count; index++)
        {
            Vector3 position = data.vertices[index].position;
            minimum = new Vector3(
                MathF.Min(minimum.x, position.x),
                MathF.Min(minimum.y, position.y),
                MathF.Min(minimum.z, position.z));
            maximum = new Vector3(
                MathF.Max(maximum.x, position.x),
                MathF.Max(maximum.y, position.y),
                MathF.Max(maximum.z, position.z));
        }

        return ((minimum + maximum) * 0.5f, (maximum - minimum) * 0.5f);
    }

}
