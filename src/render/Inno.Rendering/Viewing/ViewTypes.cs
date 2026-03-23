using Inno.Core.Mathematics;

namespace Inno.Rendering;

/// <summary>
/// Represents a viewport rectangle in pixels.
/// </summary>
public readonly record struct Viewport(int x, int y, int width, int height);

/// <summary>
/// Represents a bitmask of render layers.
/// </summary>
public readonly record struct RenderLayerMask(uint value)
{
    public static RenderLayerMask everything => new(uint.MaxValue);

    public static RenderLayerMask @default => new(1u);
}

/// <summary>
/// Represents view clear behavior.
/// </summary>
public readonly record struct ClearSettings(bool clearColor, bool clearDepth, bool clearStencil, Color color, float depth = 1.0f, byte stencil = 0)
{
    public static ClearSettings Solid(Color color) => new(true, true, false, color);
}

/// <summary>
/// Represents view culling controls.
/// </summary>
public readonly record struct CullingSettings(bool frustumCulling, bool occlusionCulling)
{
    public static CullingSettings @default => new(true, false);
}
