using System.Collections.Generic;

namespace Inno.Engine.Scene;

/// <summary>Stores one scene object's attachment and commit state.</summary>
internal sealed class SceneObjectRecord
{
    internal SceneObjectRecord(GameObject gameObject)
    {
        this.gameObject = gameObject;
    }

    internal GameObject gameObject { get; }
    internal List<GameComponent> components { get; } = [];
    internal bool isAlive { get; set; } = true;
    internal bool isCommitted { get; set; }
}
