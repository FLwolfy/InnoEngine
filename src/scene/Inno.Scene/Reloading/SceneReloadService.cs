using System;

using Inno.Extensibility.Types;
using Inno.Core.Serialization;

namespace Inno.Scene;

/// <summary>
/// Creates scene state-transfer transactions for assembly generation changes.
/// </summary>
public sealed class SceneReloadService
{
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
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="serialization"/> is null.
    /// </exception>
    public SceneReloadService(SceneWorld world, SerializationRegistry serialization)
    {
        m_world = world ?? throw new ArgumentNullException(nameof(world));
        m_serialization = serialization ?? throw new ArgumentNullException(nameof(serialization));
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
        return SceneReloadStateTransfer.Capture(m_world, context, m_serialization);
    }
}
