using System.Collections.Generic;

using Inno.Assets.Loader;
using Inno.Engine.Assets;

namespace Inno.Engine.Assets.Importers;

/// <summary>
/// Imports and exports <c>.innoprefab</c> source state.
/// </summary>
public sealed class PrefabAssetImporter : AssetImporter<PrefabAsset>
{
    private static readonly IReadOnlyList<string> s_extensions = new[] { ".innoprefab" };

    /// <inheritdoc />
    public override string importerId => "inno.engine.prefab";

    /// <inheritdoc />
    public override IReadOnlyList<string> supportedExtensions => s_extensions;

    /// <inheritdoc />
    public override AssetImportResult<PrefabAsset> ImportTyped(in AssetImportContext context)
    {
        PrefabAsset asset = PrefabAsset.Import(context.sourceBytes.ToArray(), out byte[] artifact, out string[] dependencies);
        return new AssetImportResult<PrefabAsset>(asset, artifact, dependencies);
    }

    /// <inheritdoc />
    public override bool TryExportTyped(PrefabAsset asset, out byte[] sourceBytes)
    {
        sourceBytes = asset.ExportSource();
        return true;
    }
}
