using Inno.Core.Mathematics;

namespace Inno.Rendering;

/// <summary>
/// Represents a bitmask of render layers.
/// </summary>
public readonly record struct RenderLayerMask(uint value)
{
    public static RenderLayerMask everything => new(uint.MaxValue);

    public static RenderLayerMask @default => new(1u);
}
