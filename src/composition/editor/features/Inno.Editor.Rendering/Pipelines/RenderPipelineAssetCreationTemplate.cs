using Inno.Editor.Assets;
using Inno.Rendering;

namespace Inno.Editor.Rendering;

[AssetCreationMenu(
    "inno.asset-create.render-pipeline",
    "Rendering/Render Pipeline",
    ".irenderpipeline",
    "New Render Pipeline",
    groupOrder: 200,
    itemOrder: 200,
    separatorBeforeGroup: true)]
internal sealed class RenderPipelineAssetCreationTemplate : AssetCreationTemplate<RenderPipelineAsset>;
