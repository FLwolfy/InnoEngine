using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Inno.Assets.Loader;
using Inno.Core.Mathematics;

namespace Inno.Rendering.Assets;

[AssetImporterExtension]
internal sealed class MeshAssetImporter : AssetImporter<MeshAsset>
{
    public override string importerId => "inno.rendering.mesh";

    public override IReadOnlyList<string> supportedExtensions { get; } = [".obj", ".gltf", ".glb"];

    protected override async ValueTask ImportAsync(
        AssetImportContext context,
        AssetImportWriter<MeshAsset> output,
        CancellationToken cancellationToken)
    {
        string assetRoot = ResolveAssetRoot(context);
        byte[] ReadDependency(string relativePath)
        {
            string fullPath = ResolveProjectPath(assetRoot, relativePath);
            return File.ReadAllBytes(fullPath);
        }

        MeshData data = context.extension switch
        {
            ".obj" => MeshSourceParser.ParseObj(context.relativePath, context.ReadUtf8Text()),
            ".gltf" => MeshSourceParser.ParseGltf(
                context.relativePath,
                context.sourceBytes.Span,
                isBinary: false,
                ReadDependency,
                output.DependsOnSource),
            ".glb" => MeshSourceParser.ParseGltf(
                context.relativePath,
                context.sourceBytes.Span,
                isBinary: true,
                ReadDependency,
                output.DependsOnSource),
            _ => throw new RenderingAssetFormatException(context.relativePath, "Unsupported mesh container.")
        };
        (Vector3 boundsCenter, Vector3 boundsExtents) = CalculateBounds(data);
        var asset = new MeshAsset
        {
            vertexCount = data.vertices.Count,
            indexCount = data.indices.Count,
            subMeshCount = data.subMeshes.Count,
            boundsCenter = boundsCenter,
            boundsExtents = boundsExtents
        };
        output.SetAsset(asset);
        byte[] artifact = MeshArtifactCodec.Encode(data);
        await output.WriteArtifactAsync("runtime", artifact, cancellationToken).ConfigureAwait(false);
        await output.WriteArtifactAsync("mesh", artifact, cancellationToken).ConfigureAwait(false);
    }

    private static (Vector3 center, Vector3 extents) CalculateBounds(MeshData data)
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

    private static string ResolveAssetRoot(AssetImportContext context)
    {
        string normalizedRelative = context.relativePath.Replace('/', Path.DirectorySeparatorChar);
        if (!context.absolutePath.EndsWith(normalizedRelative, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Source '{context.absolutePath}' does not end with relative path '{context.relativePath}'.");
        }

        return context.absolutePath[..^normalizedRelative.Length].TrimEnd(Path.DirectorySeparatorChar);
    }

    private static string ResolveProjectPath(string assetRoot, string relativePath)
    {
        string fullPath = Path.GetFullPath(Path.Combine(
            assetRoot,
            relativePath.Replace('/', Path.DirectorySeparatorChar)));
        string prefix = assetRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new RenderingAssetFormatException(
                relativePath,
                "Mesh dependencies must remain inside the project Assets directory.");
        }

        return fullPath;
    }
}
