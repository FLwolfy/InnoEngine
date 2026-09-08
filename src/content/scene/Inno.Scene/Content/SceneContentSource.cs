using System;
using System.Collections.Generic;
using System.Linq;
using Inno.References;

namespace Inno.Scene;

/// <summary>
/// Projects loaded scenes into the shared content protocol without depending on a rendering or audio model.
/// </summary>
public static class SceneContentSource
{
    /// <summary>
    /// Captures the ordered roots and primary identity of one scene world.
    /// </summary>
    /// <param name="world">
    /// The owner whose current scene set should be visible.
    /// </param>
    /// <returns>
    /// A caller-owned, operation-scoped identity view.
    /// </returns>
    public static ContentReadScope CreateScope(SceneWorld world)
    {
        ArgumentNullException.ThrowIfNull(world);
        return CreateScope(world.loadedScenes, world.activeScene);
    }

    /// <summary>
    /// Captures an explicit coherent scene presentation selected by a host.
    /// </summary>
    /// <param name="scenes">
    /// The ordered live scene roots.
    /// </param>
    /// <param name="activeScene">
    /// The optional primary root, which must be in the collection.
    /// </param>
    /// <returns>
    /// A caller-owned scope that retains no scene instances.
    /// </returns>
    public static ContentReadScope CreateScope(IEnumerable<GameScene> scenes, GameScene? activeScene)
    {
        ArgumentNullException.ThrowIfNull(scenes);
        return new ContentReadScope(scenes.Select(static scene => scene.identity), activeScene?.identity.persistentId);
    }
}
