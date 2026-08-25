using Inno.Editor.Interactions;

namespace Inno.Editor.Panel.Inspector;

internal static class InspectorInteractionIds
{
    internal const string C_COMPONENT_AREA = "panel/scene.inspector/component";
    internal const string C_SYSTEM_AREA = "panel/scene.inspector/system";
    internal const string C_ASSET_REFERENCE_AREA = "panel/scene.inspector/asset-reference";
    internal const string C_ENGINE_OBJECT_REFERENCE_AREA = "panel/scene.inspector/engine-object-reference";
    internal const string C_ADD_COMPONENT = "inspector/add-component";
    internal const string C_ADD_SYSTEM = "inspector/add-system";
    internal const string C_REMOVE_COMPONENT = "inspector/remove-component";
    internal const string C_REMOVE_SYSTEM = "inspector/remove-system";
    internal const string C_RESET_COMPONENT = "inspector/reset-component";
    internal const string C_RESET_SYSTEM = "inspector/reset-system";

    internal static EditorAreaId componentArea { get; } = new(C_COMPONENT_AREA);
    internal static EditorAreaId systemArea { get; } = new(C_SYSTEM_AREA);
    internal static EditorAreaId assetReferenceArea { get; } = new(C_ASSET_REFERENCE_AREA);
    internal static EditorAreaId engineObjectReferenceArea { get; } = new(C_ENGINE_OBJECT_REFERENCE_AREA);
    internal static EditorActionId addComponent { get; } = new(C_ADD_COMPONENT);
    internal static EditorActionId addSystem { get; } = new(C_ADD_SYSTEM);
    internal static EditorActionId removeComponent { get; } = new(C_REMOVE_COMPONENT);
    internal static EditorActionId removeSystem { get; } = new(C_REMOVE_SYSTEM);
    internal static EditorActionId resetComponent { get; } = new(C_RESET_COMPONENT);
    internal static EditorActionId resetSystem { get; } = new(C_RESET_SYSTEM);
}
