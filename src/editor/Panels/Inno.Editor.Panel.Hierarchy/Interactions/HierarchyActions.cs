namespace Inno.Editor.Panel.Hierarchy;

/// <summary>Defines stable action identifiers owned by the Hierarchy panel.</summary>
public static class HierarchyActions
{
    /// <summary>Creates a scene.</summary>
    public const string CreateScene = "hierarchy/create-scene";

    /// <summary>Creates an empty game object.</summary>
    public const string CreateGameObject = "hierarchy/create-game-object";

    /// <summary>Creates an empty child game object.</summary>
    public const string CreateChildGameObject = "hierarchy/create-child-game-object";

    /// <summary>Makes a scene active.</summary>
    public const string SetActiveScene = "hierarchy/set-active-scene";

    /// <summary>Begins inline renaming for the selected game object.</summary>
    public const string RenameGameObject = "hierarchy/rename-game-object";

    /// <summary>Deletes the selected game object.</summary>
    public const string DeleteGameObject = "hierarchy/delete-game-object";

    /// <summary>Deletes the selected scene when more than one scene is loaded.</summary>
    public const string DeleteScene = "hierarchy/delete-scene";
}
