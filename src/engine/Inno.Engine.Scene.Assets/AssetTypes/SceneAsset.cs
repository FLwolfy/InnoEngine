using System;
using System.IO;
using System.Linq;

using Inno.Assets;
using Inno.Assets.Core;
using Inno.Assets.Serialization;
using Inno.Core.Reflection;
using Inno.Core.Serialization;
using Inno.Engine.Scene;

namespace Inno.Engine.Scene.Assets;

/// <summary>
/// Stores imported scene source that can create independent runtime scenes.
/// </summary>
[StableTypeId("56815f6e-87bb-421b-af5f-c43b9171ce84")]
public sealed class SceneAsset : AssetObject
{
    private byte[] m_pendingPayload = [];
    private AssetDependency[] m_pendingDependencies = [];

    /// <summary>
    /// Captures a runtime scene into a new unsaved scene asset.
    /// </summary>
    /// <param name="scene">Scene to capture.</param>
    /// <returns>A scene asset ready to pass to <see cref="Inno.Assets.AssetManager.Save(AssetPath, AssetObject)"/>.</returns>
    public static SceneAsset Capture(GameScene scene)
    {
        var asset = new SceneAsset();
        asset.CaptureFrom(scene);
        return asset;
    }

    /// <summary>
    /// Replaces the pending source content with a fresh capture while preserving this asset identity.
    /// </summary>
    /// <param name="scene">Scene to capture.</param>
    public void CaptureFrom(GameScene scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        var dependencyCollection = new AssetDependencyCollection();
        SerializationContext context = SerializationContext.empty.With(dependencyCollection);
        m_pendingPayload = SerializationManager.Serialize(scene, context);
        m_pendingDependencies = dependencyCollection.dependencies.ToArray();
    }

    /// <summary>
    /// Creates an unloaded runtime scene and acquires its hard asset dependencies.
    /// </summary>
    /// <returns>A newly allocated scene.</returns>
    public GameScene Instantiate()
    {
        SerializationContext context = SerializationContext.empty.With<AssetObject>(this);
        GameScene scene = SerializationManager.Deserialize<GameScene>(GetPayload(), context);
        if (!string.IsNullOrWhiteSpace(assetPath.localPath))
            scene.name = Path.GetFileNameWithoutExtension(assetPath.localPath);
        return scene;
    }

    internal byte[] ExportSource()
        => SerializationManager.Serialize(new EngineResourceEnvelope
        {
            resourceKind = EngineResourceEnvelope.C_SCENE_KIND,
            payload = GetPayload(),
            dependencies = GetDependencies()
        });

    internal static SceneAsset Import(
        byte[] sourceBytes,
        out byte[] artifactBytes,
        out AssetDependency[] dependencies)
    {
        EngineResourceEnvelope envelope = SerializationManager.Deserialize<EngineResourceEnvelope>(sourceBytes);
        envelope.Validate(EngineResourceEnvelope.C_SCENE_KIND);
        dependencies = envelope.dependencies.ToArray();
        artifactBytes = (byte[])envelope.payload.Clone();
        return new SceneAsset { m_pendingDependencies = envelope.dependencies.ToArray() };
    }

    private byte[] GetPayload()
    {
        if (m_pendingPayload.Length != 0)
            return (byte[])m_pendingPayload.Clone();
        if (!runtimePayload.IsEmpty)
            return runtimePayload.ToArray();
        throw new InvalidOperationException($"Scene asset '{assetPath}' does not contain an imported or pending scene payload.");
    }

    private AssetDependency[] GetDependencies()
    {
        if (m_pendingDependencies.Length != 0)
            return (AssetDependency[])m_pendingDependencies.Clone();
        return AssetManager.isInitialized && identity.persistentId != Guid.Empty
            ? AssetManager.GetDependencies(this).ToArray()
            : [];
    }

    /// <inheritdoc />
    protected override void OnRuntimePayloadChanged(
        ReadOnlyMemory<byte> previousPayload,
        ReadOnlyMemory<byte> currentPayload)
    {
        m_pendingPayload = [];
        m_pendingDependencies = [];
    }
}
