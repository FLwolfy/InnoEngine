using System;
using System.Linq;

using Inno.Assets;
using Inno.Assets.Core;
using Inno.Core.Reflection;
using Inno.Core.Serialization;
using Inno.Engine.Scene;

namespace Inno.Engine.Assets;

/// <summary>
/// Stores imported scene source that can create independent runtime scenes.
/// </summary>
[StableTypeId("56815f6e-87bb-421b-af5f-c43b9171ce84")]
public sealed class SceneAsset : AssetObject
{
    private byte[] m_pendingPayload = [];

    /// <summary>
    /// Captures a runtime scene into a new unsaved scene asset.
    /// </summary>
    /// <param name="scene">Scene to capture.</param>
    /// <returns>A scene asset ready to pass to <see cref="Inno.Assets.AssetManager.Save(string, AssetObject)"/>.</returns>
    public static SceneAsset Capture(GameScene scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        var dependencyCollection = new AssetDependencyCollection();
        SerializationContext context = SerializationContext.empty.With(dependencyCollection);
        byte[] payload = SerializationManager.Serialize(scene, context);
        var asset = new SceneAsset { m_pendingPayload = payload };
        asset.SetDependencies(dependencyCollection.dependencies);
        return asset;
    }

    /// <summary>
    /// Creates an unloaded runtime scene and acquires its hard asset dependencies.
    /// </summary>
    /// <returns>A newly allocated scene.</returns>
    public GameScene Instantiate()
    {
        SerializationContext context = SerializationContext.empty.With<AssetObject>(this);
        GameScene scene = SerializationManager.Deserialize<GameScene>(GetPayload(), context);
        if (Inno.Assets.AssetManager.isInitialized && identity.persistentId != Guid.Empty)
            Inno.Assets.AssetManager.TrackDependencies(scene, this);
        return scene;
    }

    internal byte[] ExportSource()
        => SerializationManager.Serialize(new EngineResourceEnvelope
        {
            resourceKind = EngineResourceEnvelope.C_SCENE_KIND,
            payload = GetPayload(),
            dependencies = dependencies.ToArray()
        });

    internal static SceneAsset Import(byte[] sourceBytes, out byte[] artifactBytes, out string[] dependencies)
    {
        EngineResourceEnvelope envelope = SerializationManager.Deserialize<EngineResourceEnvelope>(sourceBytes);
        envelope.Validate(EngineResourceEnvelope.C_SCENE_KIND);
        dependencies = envelope.dependencies
            .Select(static dependency => dependency.lastKnownPath)
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        artifactBytes = (byte[])envelope.payload.Clone();
        var asset = new SceneAsset();
        asset.SetDependencies(envelope.dependencies);
        return asset;
    }

    private byte[] GetPayload()
    {
        if (!runtimePayload.IsEmpty)
            return runtimePayload.ToArray();
        if (m_pendingPayload.Length != 0)
            return (byte[])m_pendingPayload.Clone();
        throw new InvalidOperationException($"Scene asset '{sourcePath}' does not contain an imported or pending scene payload.");
    }
}
