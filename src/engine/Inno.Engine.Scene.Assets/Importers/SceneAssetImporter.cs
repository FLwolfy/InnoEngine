using System.Collections.Generic;

using Inno.Assets.Core;
using Inno.Assets.Loader;
using Inno.Engine.Scene.Assets;

namespace Inno.Engine.Scene.Assets.Importers;

/// <summary>
/// Imports and exports <c>.innoscene</c> source state.
/// </summary>
[AssetImporterExtension]
internal sealed class SceneAssetImporter : AssetImporter<SceneAsset>
{
    private static readonly IReadOnlyList<string> s_extensions = new[] { ".innoscene" };

    /// <inheritdoc />
    public override string importerId => "inno.engine.scene";

    /// <inheritdoc />
    public override IReadOnlyList<string> supportedExtensions => s_extensions;

    /// <inheritdoc />
    protected override AssetImportResult<SceneAsset> Import(AssetImportContext context)
    {
        SceneAsset asset = SceneAsset.Import(
            context.sourceBytes.ToArray(),
            out byte[] artifact,
            out AssetDependency[] dependencies);
        for (int i = 0; i < dependencies.Length; i++)
            context.DependsOnAsset(dependencies[i]);
        return new AssetImportResult<SceneAsset>(asset, artifact);
    }

    /// <inheritdoc />
    protected override bool TryExport(SceneAsset asset, out byte[] sourceBytes)
    {
        sourceBytes = asset.ExportSource();
        return true;
    }
}
