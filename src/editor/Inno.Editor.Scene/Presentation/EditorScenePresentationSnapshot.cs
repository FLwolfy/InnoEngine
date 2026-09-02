using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

using Inno.Scene;

namespace Inno.Editor.Scene;

/// <summary>
/// Represents one coherent, frame-scoped view of the game scenes that Editor viewports must present.
/// </summary>
/// <remarks>
/// The scene collection is immutable, while the referenced scenes remain owned by their Edit or Play
/// session. Consumers must not retain the snapshot beyond the frame in which it was captured.
/// </remarks>
public sealed class EditorScenePresentationSnapshot
{
    private readonly ReadOnlyCollection<GameScene> m_scenes;

    /// <summary>
    /// Creates a defensively copied scene presentation with a coherent active-scene reference.
    /// </summary>
    /// <param name="scenes">
    /// The ordered, non-destroyed game scenes visible to Editor viewports for one frame.
    /// </param>
    /// <param name="activeScene">
    /// The active scene, which must be present in <paramref name="scenes"/>, or
    /// <see langword="null"/> when no scene is active.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="scenes"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// Thrown when the collection contains a null or destroyed scene, or when
    /// <paramref name="activeScene"/> is not present in the collection.
    /// </exception>
    public EditorScenePresentationSnapshot(
        IEnumerable<GameScene> scenes,
        GameScene? activeScene)
    {
        ArgumentNullException.ThrowIfNull(scenes);
        GameScene[] sceneSnapshot = scenes.ToArray();
        if (sceneSnapshot.Any(static scene => scene is null || scene.isDestroyed))
        {
            throw new ArgumentException(
                "A scene presentation cannot contain null or destroyed scenes.",
                nameof(scenes));
        }
        if (activeScene is not null &&
            !sceneSnapshot.Any(scene => ReferenceEquals(scene, activeScene)))
        {
            throw new ArgumentException(
                "The active scene must belong to the presented scene collection.",
                nameof(activeScene));
        }
        m_scenes = new ReadOnlyCollection<GameScene>(sceneSnapshot);
        this.activeScene = activeScene;
    }

    /// <summary>
    /// Gets the ordered game scenes visible to Editor viewports for the captured frame.
    /// </summary>
    public IReadOnlyList<GameScene> scenes => m_scenes;

    /// <summary>
    /// Gets the active scene in <see cref="scenes"/>, or <see langword="null"/> when none is active.
    /// </summary>
    public GameScene? activeScene { get; }
}
