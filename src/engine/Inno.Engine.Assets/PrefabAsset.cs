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
    private AssetDependency[] m_pendingDependencies = [];

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
        return new PrefabAsset
        {
            m_pendingPayload = payload,
            m_pendingDependencies = dependencyCollection.dependencies.ToArray()
        };
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
        return SerializationManager.Deserialize<GameObject>(GetPayload(), context);
    }

    internal byte[] ExportSource()
        => SerializationManager.Serialize(new EngineResourceEnvelope
        {
            resourceKind = EngineResourceEnvelope.C_PREFAB_KIND,
            payload = GetPayload(),
            dependencies = GetDependencies()
        });

    internal static PrefabAsset Import(
        byte[] sourceBytes,
        out byte[] artifactBytes,
        out AssetDependency[] dependencies)
    {
        EngineResourceEnvelope envelope = SerializationManager.Deserialize<EngineResourceEnvelope>(sourceBytes);
        envelope.Validate(EngineResourceEnvelope.C_PREFAB_KIND);
        dependencies = envelope.dependencies.ToArray();
        artifactBytes = (byte[])envelope.payload.Clone();
        return new PrefabAsset { m_pendingDependencies = envelope.dependencies.ToArray() };
    }

    private byte[] GetPayload()
    {
        if (!runtimePayload.IsEmpty)
            return runtimePayload.ToArray();
        if (m_pendingPayload.Length != 0)
            return (byte[])m_pendingPayload.Clone();
        throw new InvalidOperationException($"Prefab asset '{sourcePath}' does not contain an imported or pending prefab payload.");
    }

    private AssetDependency[] GetDependencies()
    {
        if (m_pendingDependencies.Length != 0)
            return (AssetDependency[])m_pendingDependencies.Clone();
        return AssetManager.isInitialized && identity.persistentId != Guid.Empty
            ? AssetManager.GetDependencies(this).ToArray()
            : [];
    }
}
