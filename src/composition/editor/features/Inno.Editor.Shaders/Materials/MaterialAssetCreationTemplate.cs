using Inno.Editor.Assets;
using Inno.Rendering;

namespace Inno.Editor.Shaders;

[AssetCreationMenu(
    "inno.asset-create.material",
    "Rendering/Material",
    ".imaterial",
    "New Material",
    groupOrder: 200,
    itemOrder: 100,
    separatorBeforeGroup: true)]
internal sealed class MaterialAssetCreationTemplate : AssetCreationTemplate<MaterialAsset>;
