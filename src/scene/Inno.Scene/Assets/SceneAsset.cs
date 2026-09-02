using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Inno.Assets;
using Inno.Extensibility.Types;
using Inno.Core.Serialization;
using Inno.Scripting.Api;
namespace Inno.Scene;

/// <summary>
/// Stores imported scene source that can create independent runtime scenes.
/// </summary>
[StableTypeId("56815f6e-87bb-421b-af5f-c43b9171ce84")]
public sealed class SceneAsset : AssetObject
{
    private byte[] m_pendingPayload = [];
    [SerializableProperty]
    internal AssetDependency[] sourceDependencies { get; set; } = [];

    /// <summary>
    /// Captures a runtime scene into a new unsaved scene asset.
    /// </summary>
    /// <param name="scene">
    /// Scene to capture.
    /// </param>
    /// <param name="serialization">
    /// The serialization registry that owns the active scene converter generation.
    /// </param>
    /// <param name="assets">
    /// The asset database generation used to resolve references while collecting dependencies.
    /// </param>
    /// <returns>
    /// A detached scene asset that an authoring asset database can save.
    /// </returns>
    [ScriptingApiIgnore]
    public static SceneAsset Capture(
        GameScene scene,
        SerializationRegistry serialization,
        IAssetReferenceResolver assets)
    {
        var asset = new SceneAsset();
        asset.CaptureFrom(scene, serialization, assets);
        return asset;
    }

    /// <summary>
    /// Replaces the pending source content with a fresh capture while preserving this asset identity.
    /// </summary>
    /// <param name="scene">
    /// Scene to capture.
    /// </param>
    /// <param name="serialization">
    /// The serialization registry that owns the active scene converter generation.
    /// </param>
    /// <param name="assets">
    /// The asset database generation used to resolve references while collecting dependencies.
    /// </param>
    [ScriptingApiIgnore]
    public void CaptureFrom(
        GameScene scene,
        SerializationRegistry serialization,
        IAssetReferenceResolver assets)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(serialization);
        ArgumentNullException.ThrowIfNull(assets);
        var dependencyCollection = new AssetDependencyCollection();
        SerializationContext context = SerializationContext.empty
            .With(dependencyCollection)
            .With<IAssetReferenceResolver>(assets);
        m_pendingPayload = serialization.Serialize(scene, context);
        sourceDependencies = dependencyCollection.dependencies.ToArray();
    }

    /// <summary>
    /// Creates an unloaded runtime scene and acquires its hard asset dependencies.
    /// </summary>
    /// <returns>
    /// A newly allocated scene.
    /// </returns>
    /// <param name="serialization">
    /// The serialization registry that owns the active scene converter generation.
    /// </param>
    /// <param name="assets">
    /// The runtime asset generation used to resolve persistent references in the scene graph.
    /// </param>
    [ScriptingApiIgnore]
    public GameScene Instantiate(
        SerializationRegistry serialization,
        IAssetReferenceResolver assets)
    {
        ArgumentNullException.ThrowIfNull(serialization);
        ArgumentNullException.ThrowIfNull(assets);
        SerializationContext context = SerializationContext.empty
            .With<AssetObject>(this)
            .With<IAssetReferenceResolver>(assets);
        GameScene scene = serialization.Deserialize<GameScene>(GetPayload(), context);
        if (!string.IsNullOrWhiteSpace(assetPath.localPath))
            scene.name = Path.GetFileNameWithoutExtension(assetPath.localPath);
        return scene;
    }

    /// <summary>
    /// Creates an imported scene asset from validated runtime content produced by the scene asset pipeline.
    /// </summary>
    /// <param name="payload">
    /// The complete serialized runtime scene graph.
    /// </param>
    /// <param name="dependencies">
    /// The direct persistent asset dependencies captured with the scene graph.
    /// </param>
    /// <returns>
    /// A scene asset ready for an asset import writer to commit.
    /// </returns>
    [ScriptingApiIgnore]
    public static SceneAsset CreateImported(
        ReadOnlySpan<byte> payload,
        IReadOnlyList<AssetDependency> dependencies)
    {
        ArgumentNullException.ThrowIfNull(dependencies);
        if (payload.IsEmpty)
            throw new ArgumentException("Imported scene content cannot be empty.", nameof(payload));
        return new SceneAsset
        {
            m_pendingPayload = payload.ToArray(),
            sourceDependencies = dependencies.ToArray()
        };
    }

    /// <summary>
    /// Captures the immutable runtime payload and dependency descriptors required by the scene asset pipeline.
    /// </summary>
    /// <returns>
    /// A detached authoring-content snapshot owned by the caller.
    /// </returns>
    [ScriptingApiIgnore]
    public EngineAssetContent CaptureContent()
        => new(GetPayload(), sourceDependencies);

    private byte[] GetPayload()
    {
        if (m_pendingPayload.Length != 0)
            return (byte[])m_pendingPayload.Clone();
        if (!runtimePayload.IsEmpty)
            return runtimePayload.ToArray();
        throw new InvalidOperationException($"Scene asset '{assetPath}' does not contain an imported or pending scene payload.");
    }

    /// <summary>
    /// Rebuilds runtime-derived state after the serialized asset payload changes.
    /// </summary>
    /// <param name="previousPayload">
    /// The previous payload consumed by on runtime payload changed; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <param name="currentPayload">
    /// The current payload consumed by on runtime payload changed; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    protected override void OnRuntimePayloadChanged(
        ReadOnlyMemory<byte> previousPayload,
        ReadOnlyMemory<byte> currentPayload)
    {
        m_pendingPayload = [];
    }
}
