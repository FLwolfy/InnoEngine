using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Inno.Assets.Core;
using Inno.Assets.Loader;
using Inno.Assets.Serialization;

namespace Inno.Rendering.Assets;

[AssetImporterExtension]
internal sealed class MaterialAssetImporter : AssetImporter<MaterialAsset>
{
    public override string importerId => "inno.rendering.material";

    public override IReadOnlyList<string> supportedExtensions { get; } = [".imaterial"];

    protected override async ValueTask ImportAsync(
        AssetImportContext context,
        AssetImportWriter<MaterialAsset> output,
        CancellationToken cancellationToken)
    {
        MaterialAsset asset = NativeAssetSourceSerialization.Import<MaterialAsset>(
            context.sourceBytes.Span,
            out IReadOnlyList<AssetDependency> dependencies);
        foreach (AssetDependency dependency in dependencies)
            output.DependsOnAsset(dependency);
        Validate(asset);
        output.SetAsset(asset);
        await output.WriteArtifactAsync("runtime", context.sourceBytes, cancellationToken)
            .ConfigureAwait(false);
    }

    protected override ValueTask<ReadOnlyMemory<byte>?> ExportAsync(
        MaterialAsset asset,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Validate(asset);
        return ValueTask.FromResult<ReadOnlyMemory<byte>?>(NativeAssetSourceSerialization.Export(asset));
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
