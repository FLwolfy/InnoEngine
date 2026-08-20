namespace Inno.Editor.Panel.Inspector;

/// <summary>Defines stable action identifiers owned by the Inspector panel.</summary>
public static class InspectorActions
{
    /// <summary>Adds a Component type supplied as the action argument.</summary>
    public const string AddComponent = "inspector/add-component";

    /// <summary>Resets a Component to its declared defaults.</summary>
    public const string ResetComponent = "inspector/reset-component";

    /// <summary>Removes a Component from its GameObject.</summary>
    public const string RemoveComponent = "inspector/remove-component";

    /// <summary>Adds a GameSystem type supplied as the action argument.</summary>
    public const string AddSystem = "inspector/add-system";

    /// <summary>Resets a GameSystem to its declared defaults.</summary>
    public const string ResetSystem = "inspector/reset-system";

    /// <summary>Removes a GameSystem from its Scene.</summary>
    public const string RemoveSystem = "inspector/remove-system";
}
