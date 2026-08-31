using System.Collections.Generic;

namespace Inno.Engine.Scene;

/// <summary>Captures the stable attachment order for one scene object.</summary>
internal sealed record SceneObjectStructureSnapshot(
    GameObject gameObject,
    IReadOnlyList<GameComponent> components);

internal sealed record SceneStructureSnapshot(
    IReadOnlyList<SceneObjectStructureSnapshot> objects);
