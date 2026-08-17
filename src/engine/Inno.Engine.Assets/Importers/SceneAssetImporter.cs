using System.Collections.Generic;

using Inno.Assets.Loader;
using Inno.Engine.Assets;

namespace Inno.Engine.Assets.Importers;

/// <summary>
/// Imports and exports <c>.innoscene</c> source state.
/// </summary>
public sealed class SceneAssetImporter : AssetImporter<SceneAsset>
{
    private static readonly IReadOnlyList<string> s_extensions = new[] { ".innoscene" };

    /// <inheritdoc />
    public override string importerId => "inno.engine.scene";

    /// <inheritdoc />
    public override IReadOnlyList<string> supportedExtensions => s_extensions;

    /// <inheritdoc />
    public override AssetImportResult<SceneAsset> ImportTyped(in AssetImportContext context)
    {
        SceneAsset asset = SceneAsset.Import(context.sourceBytes.ToArray(), out byte[] artifact, out string[] dependencies);
        return new AssetImportResult<SceneAsset>(asset, artifact, dependencies);
    }

    /// <inheritdoc />
    public override bool TryExportTyped(SceneAsset asset, out byte[] sourceBytes)
    {
        sourceBytes = asset.ExportSource();
        return true;
    }
}
