namespace Inno.Editor.Panel.Inspector;

/// <summary>Defines stable interaction areas owned by the Inspector panel.</summary>
public static class InspectorAreas
{
    /// <summary>Identifies Component cards and the Add Component menu.</summary>
    public const string Component = "panel/scene.inspector/component";

    /// <summary>Identifies GameSystem cards and the Add System menu.</summary>
    public const string System = "panel/scene.inspector/system";

    /// <summary>Identifies EngineObject-reference property targets.</summary>
    public const string EngineObjectReference = "panel/scene.inspector/engine-object-reference";

    /// <summary>Identifies AssetObject-reference property targets.</summary>
    public const string AssetReference = "panel/scene.inspector/asset-reference";
}
