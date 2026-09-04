using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Inno.Assets;
using Inno.Extensibility.Types;
using Inno.Core.Serialization;
using Inno.Scene.Components;
using Inno.Scripting.Api;

namespace Inno.Scene;

/// <summary>
/// Stores a persistent game object subtree that can be instantiated repeatedly.
/// </summary>
[StableTypeId("21d5a292-cc2a-4c79-879d-e4ca5ca6844f")]
public sealed class PrefabAsset : AssetObject
{
    private byte[] m_pendingPayload = [];
    [SerializableProperty]
    internal AssetDependency[] sourceDependencies { get; set; } = [];

    /// <summary>
    /// Captures a game object subtree into a new unsaved prefab asset.
    /// </summary>
    /// <param name="root">
    /// Prefab source root.
    /// </param>
    /// <param name="serialization">
    /// The serialization registry that owns the active prefab converter generation.
    /// </param>
    /// <param name="assets">
    /// The runtime asset generation used to resolve persistent references in the prefab graph.
    /// </param>
    /// <returns>
    /// A prefab asset ready to save.
    /// </returns>
    [ScriptingApiIgnore]
    public static PrefabAsset Capture(
        GameObject root,
        SerializationRegistry serialization,
        IAssetReferenceResolver assets)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(serialization);
        ArgumentNullException.ThrowIfNull(assets);
        var dependencyCollection = new AssetDependencyCollection();
        SerializationContext context = SerializationContext.empty
            .With(dependencyCollection)
            .With<IAssetReferenceResolver>(assets);
        byte[] payload = serialization.Serialize(root, context);
        return new PrefabAsset
        {
            m_pendingPayload = payload,
            sourceDependencies = dependencyCollection.dependencies.ToArray()
        };
    }

    /// <summary>
    /// Instantiates this prefab into a scene using newly generated object identities.
    /// </summary>
    /// <param name="scene">
    /// Target scene.
    /// </param>
    /// <param name="serialization">
    /// The serialization registry that owns the active prefab converter generation.
    /// </param>
    /// <param name="assets">
    /// The runtime asset generation used to resolve persistent references in the prefab graph.
    /// </param>
    /// <param name="parent">
    /// Optional target parent.
    /// </param>
    /// <returns>
    /// The instantiated root game object.
    /// </returns>
    [ScriptingApiIgnore]
    public GameObject Instantiate(
        GameScene scene,
        SerializationRegistry serialization,
        IAssetReferenceResolver assets,
        Transform? parent = null)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(serialization);
        ArgumentNullException.ThrowIfNull(assets);
        SerializationContext context = SerializationContext.empty
            .With(scene)
            .With<AssetObject>(this)
            .With<IAssetReferenceResolver>(assets);
        if (parent is not null)
            context = context.With(parent);
        GameObject root = serialization.Deserialize<GameObject>(GetPayload(), context);
        if (!string.IsNullOrWhiteSpace(assetPath.localPath))
            root.name = Path.GetFileNameWithoutExtension(assetPath.localPath);
        return root;
    }

    /// <summary>
    /// Creates an imported prefab asset from validated runtime content produced by the prefab asset pipeline.
    /// </summary>
    /// <param name="payload">
    /// The complete serialized runtime object subtree.
    /// </param>
    /// <param name="dependencies">
    /// The direct persistent asset dependencies captured with the subtree.
    /// </param>
    /// <returns>
    /// A prefab asset ready for an asset import writer to commit.
    /// </returns>
    [ScriptingApiIgnore]
    public static PrefabAsset CreateImported(
        ReadOnlySpan<byte> payload,
        IReadOnlyList<AssetDependency> dependencies)
    {
        ArgumentNullException.ThrowIfNull(dependencies);
        if (payload.IsEmpty)
            throw new ArgumentException("Imported prefab content cannot be empty.", nameof(payload));
        return new PrefabAsset
        {
            m_pendingPayload = payload.ToArray(),
            sourceDependencies = dependencies.ToArray()
        };
    }

    /// <summary>
    /// Captures the immutable runtime payload and dependency descriptors required by the prefab asset pipeline.
    /// </summary>
    /// <returns>
    /// A detached authoring-content snapshot owned by the caller.
    /// </returns>
    [ScriptingApiIgnore]
    public EngineAssetContent CaptureContent()
        => new(GetPayload(), sourceDependencies);

    private byte[] GetPayload()
    {
        if (!runtimePayload.IsEmpty)
            return runtimePayload.ToArray();
        if (m_pendingPayload.Length != 0)
            return (byte[])m_pendingPayload.Clone();
        throw new InvalidOperationException($"Prefab asset '{assetPath}' does not contain an imported or pending prefab payload.");
    }

}
