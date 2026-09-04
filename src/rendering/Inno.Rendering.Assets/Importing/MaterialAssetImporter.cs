using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Inno.Assets;
using Inno.Assets.Pipeline;
using Inno.Rendering;

namespace Inno.Rendering.Assets;

[AssetImporterExtension]
internal sealed class MaterialAssetImporter : AssetImporter<MaterialAsset>
{
    /// <summary>
    /// Gets the stable importer identity used in artifact fingerprints.
    /// </summary>
    public override string importerId => "inno.rendering.material";

    /// <summary>
    /// Gets the normalized source extensions accepted by this importer.
    /// </summary>
    public override IReadOnlyList<string> supportedExtensions { get; } = [".imaterial"];

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
        AssetImportWriter<MaterialAsset> output,
        CancellationToken cancellationToken)
    {
        MaterialAsset asset = NativeAssetSourceSerialization.Import<MaterialAsset>(
            context.sourceBytes.Span,
            context.services,
            out IReadOnlyList<AssetDependency> dependencies);
        foreach (AssetDependency dependency in dependencies)
            output.DependsOnAsset(dependency);
        Validate(asset);
        output.SetAsset(asset);
        await output.WriteArtifactAsync("runtime", context.sourceBytes, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Writes a validated asset representation to its writable source mount.
    /// </summary>
    /// <param name="context">
    /// The operation scope that provides state, services, and ownership boundaries.
    /// </param>
    /// <param name="asset">
    /// The validated asset instance exported by this operation.
    /// </param>
    /// <param name="cancellationToken">
    /// The token that cancels the operation before it commits.
    /// </param>
    /// <returns>
    /// An asynchronous operation that completes after all requested work has finished.
    /// </returns>
    protected override ValueTask<ReadOnlyMemory<byte>?> ExportAsync(
        AssetExportContext context,
        MaterialAsset asset,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Validate(asset);
        return ValueTask.FromResult<ReadOnlyMemory<byte>?>(NativeAssetSourceSerialization.Export(
            asset,
            context.services));
    }

    private static void Validate(MaterialAsset asset)
    {
        ShaderDefinition definition = asset.shader?.definition
            ?? throw new InvalidOperationException("A material requires a shader with a committed definition.");
        Dictionary<ShaderPropertyId, ShaderPropertyDefinition> properties = definition.properties
            .ToDictionary(static property => property.id);
        foreach (MaterialPropertyEntry property in asset.properties)
        {
            if (!properties.TryGetValue(property.id, out ShaderPropertyDefinition shaderProperty))
                throw new InvalidOperationException($"Material property '{property.id}' is not declared by its shader.");
            if (shaderProperty.bindingKind is ShaderPropertyBindingKind.StorageTexture
                or ShaderPropertyBindingKind.StorageBuffer)
            {
                throw new InvalidOperationException(
                    $"Material property '{property.id}' is Pipeline-owned storage state and cannot be persisted by a material.");
            }
            if (!IsCompatible(shaderProperty.type, property.value.kind))
            {
                throw new InvalidOperationException(
                    $"Material property '{property.id}' value '{property.value.kind}' is incompatible with " +
                    $"shader type '{shaderProperty.type}'.");
            }
        }
        HashSet<string> options = definition.keywords
            .SelectMany(static keyword => keyword.options)
            .ToHashSet(StringComparer.Ordinal);
        string? unknownKeyword = asset.keywords.FirstOrDefault(keyword => !options.Contains(keyword));
        if (unknownKeyword is not null)
            throw new InvalidOperationException($"Material keyword '{unknownKeyword}' is not declared by its shader.");
    }

    private static bool IsCompatible(ShaderPropertyType propertyType, MaterialValueKind valueKind)
        => propertyType switch
        {
            ShaderPropertyType.Float => valueKind == MaterialValueKind.Float,
            ShaderPropertyType.Vector2 or ShaderPropertyType.Vector3 or ShaderPropertyType.Vector4 =>
                valueKind == MaterialValueKind.Vector,
            ShaderPropertyType.Color => valueKind == MaterialValueKind.Color,
            ShaderPropertyType.Matrix4x4 => valueKind == MaterialValueKind.Matrix,
            ShaderPropertyType.Texture2D
                or ShaderPropertyType.Texture2DArray
                or ShaderPropertyType.Texture3D
                or ShaderPropertyType.TextureCube =>
                valueKind == MaterialValueKind.Texture,
            _ => false
        };
}
