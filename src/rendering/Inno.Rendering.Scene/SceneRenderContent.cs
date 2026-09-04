using System;
using System.Collections.Generic;

using Inno.Scene;

namespace Inno.Rendering.Scene;

/// <summary>
/// Projects a scene world into the model-neutral content scope consumed by rendering extensions.
/// </summary>
public static class SceneRenderContent
{
    /// <summary>
    /// Creates an immutable frame-scoped rendering view of a scene world.
    /// </summary>
    /// <param name="world">
    /// The scene world whose loaded scenes define the ordered rendering content.
    /// </param>
    /// <returns>
    /// A rendering content scope containing every loaded scene and the active scene identity, when present.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="world"/> is <see langword="null"/>.
    /// </exception>
    public static RenderContentScope CreateScope(SceneWorld world)
    {
        ArgumentNullException.ThrowIfNull(world);
        IReadOnlyList<GameScene> scenes = world.loadedScenes;
        var contents = new RenderContentReference[scenes.Count];
        RenderContentId? activeContent = null;
        for (int index = 0; index < scenes.Count; index++)
        {
            GameScene scene = scenes[index];
            var contentId = new RenderContentId(scene.identity.persistentId);
            contents[index] = new RenderContentReference(contentId, scene);
            if (ReferenceEquals(scene, world.activeScene))
                activeContent = contentId;
        }
        return new RenderContentScope(contents, activeContent);
    }
}
