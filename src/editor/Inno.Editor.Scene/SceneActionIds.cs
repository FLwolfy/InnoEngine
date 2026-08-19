namespace Inno.Editor.Scene;

/// <summary>Defines stable action identifiers owned by the Scene editor feature.</summary>
public static class SceneActionIds
{
    /// <summary>Creates a scene.</summary>
    public const string CreateScene = "scene.create";

    /// <summary>Creates an empty game object.</summary>
    public const string CreateGameObject = "scene.create-game-object";

    /// <summary>Creates an empty child game object.</summary>
    public const string CreateChildGameObject = "scene.create-child-game-object";

    /// <summary>Makes a scene active.</summary>
    public const string SetActiveScene = "scene.set-active";

    /// <summary>Adds a component type supplied as the action argument.</summary>
    public const string AddComponent = "scene.add-component";

    /// <summary>Adds a system type supplied as the action argument.</summary>
    public const string AddSystem = "scene.add-system";
}
