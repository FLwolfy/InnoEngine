using System;

using Inno.Core.Reflection;

namespace Inno.Engine.Scene.Assets;

/// <summary>
/// Creates scene migration transactions for assembly generation changes.
/// </summary>
public static class SceneReloadService
{
    /// <summary>
    /// Captures all loaded scene objects affected by a prepared type-cache reload.
    /// </summary>
    /// <param name="context">The prepared type-cache reload context.</param>
    /// <returns>A staged scene migration that has not modified live objects.</returns>
    public static ISceneReloadMigration Capture(TypeCacheReloadContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return SceneHotReloadMigration.Capture(context);
    }
}
