using System;
using System.Collections.Generic;

namespace Inno.Engine.Scene;

internal sealed record RestoredSceneGraph(
    IReadOnlyDictionary<Guid, GameObject> objects,
    IReadOnlyDictionary<Guid, GameComponent> components);
