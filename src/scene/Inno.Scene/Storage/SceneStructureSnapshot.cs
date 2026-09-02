using System.Collections.Generic;

namespace Inno.Scene;

/// <summary>
/// Captures the stable attachment order for one scene object.
/// </summary>
/// <param name="gameObject">
/// The game object used to initialize this instance.
/// </param>
/// <param name="components">
/// The components used to initialize this instance.
/// </param>
internal sealed record SceneObjectStructureSnapshot(
    GameObject gameObject,
    IReadOnlyList<GameComponent> components);

internal sealed record SceneStructureSnapshot(
    IReadOnlyList<SceneObjectStructureSnapshot> objects);
