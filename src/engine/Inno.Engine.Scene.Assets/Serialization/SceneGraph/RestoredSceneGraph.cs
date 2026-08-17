using System;
using System.Collections.Generic;

using Inno.Engine.Scene;

namespace Inno.Engine.Scene.Assets;

internal sealed record RestoredSceneGraph(
    IReadOnlyDictionary<Guid, GameObject> objects,
    IReadOnlyDictionary<Guid, GameComponent> components);
