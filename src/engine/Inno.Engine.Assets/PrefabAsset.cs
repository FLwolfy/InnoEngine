using System;
using System.Linq;

using Inno.Assets;
using Inno.Assets.Core;
using Inno.Core.Reflection;
using Inno.Core.Serialization;
using Inno.Engine.Scene;
using Inno.Engine.Scene.Components;

namespace Inno.Engine.Assets;

/// <summary>
/// Stores a persistent game object subtree that can be instantiated repeatedly.
/// </summary>
[StableTypeId("21d5a292-cc2a-4c79-879d-e4ca5ca6844f")]
public sealed class PrefabAsset : AssetObject
{
    private byte[] m_pendingPayload = [];

    /// <summary>
    /// Captures a game object subtree into a new unsaved prefab asset.
    /// </summary>
    /// <param name="root">Prefab source root.</param>
    /// <returns>A prefab asset ready to save.</returns>
    public static PrefabAsset Capture(GameObject root)
    {
        ArgumentNullException.ThrowIfNull(root);
        var dependencyCollection = new AssetDependencyCollection();
        SerializationContext context = SerializationContext.empty.With(dependencyCollection);
        byte[] payload = SerializationManager.Serialize(root, context);
        var asset = new PrefabAsset { m_pendingPayload = payload };
        asset.SetDependencies(dependencyCollection.dependencies);
        return asset;
    }

    /// <summary>
    /// Instantiates this prefab into a scene using newly generated object identities.
    /// </summary>
    /// <param name="scene">Target scene.</param>
    /// <param name="parent">Optional target parent.</param>
    /// <returns>The instantiated root game object.</returns>
    public GameObject Instantiate(GameScene scene, Transform? parent = null)
    {
        ArgumentNullException.ThrowIfNull(scene);
        SerializationContext context = SerializationContext.empty
            .With(scene)
            .With<AssetObject>(this);
        if (parent is not null)
            context = context.With(parent);
        GameObject root = SerializationManager.Deserialize<GameObject>(GetPayload(), context);
        if (Inno.Assets.AssetManager.isInitialized && identity.persistentId != Guid.Empty)
            Inno.Assets.AssetManager.TrackDependencies(scene, this);
        return root;
    }

    internal byte[] ExportSource()
        => SerializationManager.Serialize(new EngineResourceEnvelope
        {
            resourceKind = EngineResourceEnvelope.C_PREFAB_KIND,
            payload = GetPayload(),
            dependencies = dependencies.ToArray()
        });

    internal static PrefabAsset Import(byte[] sourceBytes, out byte[] artifactBytes, out string[] dependencies)
    {
        EngineResourceEnvelope envelope = SerializationManager.Deserialize<EngineResourceEnvelope>(sourceBytes);
        envelope.Validate(EngineResourceEnvelope.C_PREFAB_KIND);
        dependencies = envelope.dependencies
            .Select(static dependency => dependency.lastKnownPath)
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        artifactBytes = (byte[])envelope.payload.Clone();
        var asset = new PrefabAsset();
        asset.SetDependencies(envelope.dependencies);
        return asset;
    }

    private byte[] GetPayload()
    {
        if (!runtimePayload.IsEmpty)
            return runtimePayload.ToArray();
        if (m_pendingPayload.Length != 0)
            return (byte[])m_pendingPayload.Clone();
        throw new InvalidOperationException($"Prefab asset '{sourcePath}' does not contain an imported or pending prefab payload.");
    }
}
