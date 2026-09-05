using System;
using System.Linq;
using Inno.Scene;

namespace Inno.Audio.Scene;

/// <summary>
/// Adapts the currently loaded scenes in one world into an update-scoped audio content boundary.
/// </summary>
public sealed class SceneAudioContent
{
    private readonly SceneWorld m_world;

    /// <summary>
    /// Creates a scene audio content adapter for one isolated scene world.
    /// </summary>
    /// <param name="world">
    /// Scene world whose loaded scenes are captured on demand.
    /// </param>
    public SceneAudioContent(SceneWorld world)
    {
        m_world = world ?? throw new ArgumentNullException(nameof(world));
    }

    /// <summary>
    /// Captures loaded scenes without retaining component or plugin-generation objects beyond the update.
    /// </summary>
    /// <returns>
    /// An immutable content scope ordered by the scene world's hierarchy order.
    /// </returns>
    public AudioContentScope Capture()
        => new(m_world.loadedScenes.Select(static scene => new AudioContentReference(
            new AudioContentId(scene.identity.persistentId),
            scene)));
}
