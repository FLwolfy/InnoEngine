using System;

using Inno.Core.Serialization;
using Inno.Engine.Scene;
using Inno.Engine.Scene.Components;

namespace Inno.Engine.Scene.Assets;

/// <summary>
/// Captures and restores one GameObject subtree with its persistent object and component identities.
/// </summary>
public static class SceneSubtreeSerialization
{
    /// <summary>
    /// Captures one live GameObject and all descendants without serializing unrelated scene objects.
    /// </summary>
    /// <param name="root">The live subtree root.</param>
    /// <returns>Neutral bytes containing the subtree structure, components, properties, and identity references.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="root"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the root is destroyed or detached.</exception>
    public static byte[] Capture(GameObject root)
    {
        ArgumentNullException.ThrowIfNull(root);
        if (!root.isRuntimeValid)
            throw new InvalidOperationException("A destroyed or detached GameObject subtree cannot be captured.");
        return SerializationManager.Serialize(new SceneSubtreeState(root));
    }

    /// <summary>
    /// Restores a previously captured subtree into a loaded scene with its original persistent identities.
    /// </summary>
    /// <param name="scene">The loaded destination scene.</param>
    /// <param name="data">Bytes created by <see cref="Capture"/>.</param>
    /// <param name="parent">The optional external parent restored after the subtree is created.</param>
    /// <param name="siblingIndex">The requested sibling index under <paramref name="parent"/> or at the scene root.</param>
    /// <returns>The restored subtree root.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="scene"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the scene is unavailable or an identity conflicts.</exception>
    public static GameObject Restore(
        GameScene scene,
        ReadOnlySpan<byte> data,
        Transform? parent,
        int siblingIndex)
    {
        ArgumentNullException.ThrowIfNull(scene);
        if (!scene.isLoaded || scene.isDestroyed)
            throw new InvalidOperationException("A scene subtree can only be restored into a live loaded scene.");
        if (parent is not null && !ReferenceEquals(parent.gameObject.scene, scene))
            throw new ArgumentException("The requested parent belongs to another scene.", nameof(parent));
        SerializationContext context = SerializationContext.empty.With(scene);
        SceneSubtreeState state = SerializationManager.Deserialize<SceneSubtreeState>(data, context);
        try
        {
            state.root.transform.SetParent(parent);
            state.root.transform.SetSiblingIndex(siblingIndex);
            return state.root;
        }
        catch
        {
            if (state.root.isRuntimeValid)
                _ = scene.DestroyObject(state.root);
            throw;
        }
    }
}
