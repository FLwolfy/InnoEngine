using Inno.References;

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
    /// A caller-owned identity scope for the Edit scenes while not playing and the isolated Play scenes
    /// after Play Mode commits. Dispose it after consumption; it retains no scene instances and stale
    /// identities never resolve to a replacement session or generation.
    /// </returns>
    ContentReadScope Capture();
}
