using Inno.Core.Mathematics;

namespace Inno.Rendering;

/// <summary>
/// Represents view culling controls.
/// </summary>
public readonly record struct CullingSettings(bool frustumCulling, bool occlusionCulling)
{
    public static CullingSettings @default => new(true, false);
}
