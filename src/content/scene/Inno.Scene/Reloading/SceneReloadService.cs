using System;

using Inno.Assets;
using Inno.Extensibility.Types;
using Inno.Core.Serialization;

namespace Inno.Scene;

/// <summary>
/// Creates scene state-transfer transactions for assembly generation changes.
/// </summary>
public sealed class SceneReloadService
{
    private readonly IAssetReferenceResolver m_assets;
    private readonly SerializationRegistry m_serialization;
    private readonly SceneWorld m_world;

    /// <summary>
    /// Creates a scene reload service bound to one serialization generation owner.
    /// </summary>
    /// <param name="serialization">
    /// The serialization registry used to capture and restore scene state.
    /// </param>
    /// <param name="world">
    /// The isolated scene world whose active generation is captured.
    /// </param>
    /// <param name="assets">
    /// The asset reference generation used throughout capture, activation, and rollback.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="world"/>, <paramref name="serialization"/>, or
    /// <paramref name="assets"/> is null.
    /// </exception>
    public SceneReloadService(
        SceneWorld world,
        SerializationRegistry serialization,
        IAssetReferenceResolver assets)
    {
        m_world = world ?? throw new ArgumentNullException(nameof(world));
        m_serialization = serialization ?? throw new ArgumentNullException(nameof(serialization));
        m_assets = assets ?? throw new ArgumentNullException(nameof(assets));
    }

    /// <summary>
    /// Captures all loaded scene objects affected by a prepared type-cache reload.
    /// </summary>
    /// <param name="context">
    /// The prepared type-cache reload context.
    /// </param>
    /// <returns>
    /// A staged state transfer that has not modified live objects.
    /// </returns>
    public ISceneReloadStateTransfer Capture(TypeCacheReloadContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return new SceneReloadRecovery(
            SceneReloadStateTransfer.Capture(m_world, context, m_serialization, m_assets),
            m_world, context, m_assets);
    }
}
