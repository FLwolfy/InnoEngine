using System;
using System.Collections.Generic;

using Inno.Assets;
using Inno.Core.Serialization;
using Inno.Scene;
using Inno.Scene.Components;

namespace Inno.Scene;

/// <summary>
/// Captures and restores one GameObject subtree with its persistent object and component identities.
/// </summary>
public static class SceneSubtreeSerialization
{
    /// <summary>
    /// Captures one live GameObject and all descendants without serializing unrelated scene objects.
    /// </summary>
    /// <param name="root">
    /// The live subtree root.
    /// </param>
    /// <param name="serialization">
    /// The serialization registry that owns the active converter generation.
    /// </param>
    /// <param name="assets">
    /// The asset-reference resolver used to encode referenced asset identities.
    /// </param>
    /// <returns>
    /// Neutral bytes containing the subtree structure, components, properties, and identity references.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="root"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the root is destroyed or detached.
    /// </exception>
    public static byte[] Capture(
        GameObject root,
        SerializationRegistry serialization,
        IAssetReferenceResolver assets)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(serialization);
        ArgumentNullException.ThrowIfNull(assets);
        if (!root.isRuntimeValid)
            throw new InvalidOperationException("A destroyed or detached GameObject subtree cannot be captured.");
        SerializationContext context = SerializationContext.empty
            .With<IAssetReferenceResolver>(assets);
        return serialization.Serialize(new SceneSubtreeState(root), context);
    }

    /// <summary>
    /// Restores a previously captured subtree into a loaded scene with its original persistent identities.
    /// </summary>
    /// <param name="scene">
    /// The loaded destination scene.
    /// </param>
    /// <param name="data">
    /// Bytes created by <see cref="Capture"/>.
    /// </param>
    /// <param name="parent">
    /// The optional external parent restored after the subtree is created.
    /// </param>
    /// <param name="siblingIndex">
    /// The requested sibling index under <paramref name="parent"/> or at the scene root.
    /// </param>
    /// <param name="serialization">
    /// The serialization registry that owns the active converter generation.
    /// </param>
    /// <param name="assets">
    /// The asset-reference resolver used to restore referenced asset identities.
    /// </param>
    /// <returns>
    /// The restored subtree root.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="scene"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the scene is unavailable or an identity conflicts.
    /// </exception>
    public static GameObject Restore(
        GameScene scene,
        ReadOnlySpan<byte> data,
        SerializationRegistry serialization,
        IAssetReferenceResolver assets,
        Transform? parent,
        int siblingIndex)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(serialization);
        ArgumentNullException.ThrowIfNull(assets);
        if (!scene.isLoaded || scene.isDestroyed)
            throw new InvalidOperationException("A scene subtree can only be restored into a live loaded scene.");
        if (parent is not null && !ReferenceEquals(parent.gameObject.scene, scene))
            throw new ArgumentException("The requested parent belongs to another scene.", nameof(parent));
        var existing = new HashSet<GameObject>(
            scene.GetObjects(),
            ReferenceEqualityComparer.Instance);
        try
        {
            SerializationContext context = SerializationContext.empty
                .With(scene)
                .With<IAssetReferenceResolver>(assets);
            SceneSubtreeState state = serialization.Deserialize<SceneSubtreeState>(data, context);
            state.root.transform.SetParent(parent);
            state.root.transform.SetSiblingIndex(siblingIndex);
            return state.root;
        }
        catch (Exception exception)
        {
            SceneRestoreCompensation.RethrowAfterRemovingCreatedObjects(
                exception,
                scene,
                existing,
                "Scene subtree restoration");
            throw;
        }
    }
}
