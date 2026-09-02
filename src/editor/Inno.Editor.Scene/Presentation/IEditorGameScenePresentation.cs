namespace Inno.Editor.Scene;

/// <summary>
/// Supplies the scene set that represents the game to Editor viewport consumers without exposing
/// runtime-session ownership.
/// </summary>
public interface IEditorGameScenePresentation
{
    /// <summary>
    /// Captures one coherent game-scene presentation for the current Editor frame.
    /// </summary>
    /// <returns>
    /// An immutable collection snapshot that references the Edit scenes while not playing and the
    /// isolated Play scenes after Play Mode has committed successfully.
    /// </returns>
    EditorScenePresentationSnapshot Capture();
}
