using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Inno.Assets;
using Inno.Assets.Core;
using Inno.Assets.File;
using Inno.Assets.Loader;
using Inno.Core.Mathematics;

namespace Inno.Rendering.Assets;

[AssetImporterExtension]
internal sealed class GeometryAssetImporter : AssetImporter<GeometryAsset>
{
    public override string importerId => "inno.rendering.geometry";

    public override IReadOnlyList<string> supportedExtensions { get; } = [".obj", ".gltf", ".glb"];

    protected override async ValueTask ImportAsync(
        AssetImportContext context,
        AssetImportWriter<GeometryAsset> output,
        CancellationToken cancellationToken)
    {
        byte[] ReadDependency(string relativePath)
        {
            AssetPath dependency = AssetPath.Parse(relativePath);
            AssetSourceMount mount = AssetManager.sourceMounts.Single(candidate => candidate.id == dependency.source);
            return File.ReadAllBytes(mount.Resolve(dependency.localPath));
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
        var asset = new GeometryAsset
        {
            vertexCount = data.vertices.Count,
            indexCount = data.indices.Count,
            sectionCount = data.sections.Count,
            boundsCenter = boundsCenter,
            boundsExtents = boundsExtents
        };
        output.SetAsset(asset);
        byte[] artifact = GeometryArtifactCodec.Encode(data);
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
