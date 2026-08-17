using System.Collections.Generic;

using Inno.Assets.Core;
using Inno.Assets.Loader;
using Inno.Engine.Scene.Assets;

namespace Inno.Engine.Scene.Assets.Importers;

/// <summary>
/// Imports and exports <c>.innoprefab</c> source state.
/// </summary>
[AssetImporterExtension]
internal sealed class PrefabAssetImporter : AssetImporter<PrefabAsset>
{
    private static readonly IReadOnlyList<string> s_extensions = new[] { ".innoprefab" };

    /// <inheritdoc />
    public override string importerId => "inno.engine.prefab";

    /// <inheritdoc />
    public override IReadOnlyList<string> supportedExtensions => s_extensions;

    /// <inheritdoc />
    protected override AssetImportResult<PrefabAsset> Import(AssetImportContext context)
    {
        PrefabAsset asset = PrefabAsset.Import(
            context.sourceBytes.ToArray(),
            out byte[] artifact,
            out AssetDependency[] dependencies);
        for (int i = 0; i < dependencies.Length; i++)
            context.DependsOnAsset(dependencies[i]);
        return new AssetImportResult<PrefabAsset>(asset, artifact);
    }

    /// <inheritdoc />
    protected override bool TryExport(PrefabAsset asset, out byte[] sourceBytes)
    {
        sourceBytes = asset.ExportSource();
        return true;
    }
}
