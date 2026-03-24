using Inno.Core.Mathematics;

namespace Inno.Rendering;

/// <summary>
/// Represents view clear behavior.
/// </summary>
public readonly record struct ClearSettings(bool clearColor, bool clearDepth, bool clearStencil, Color color, float depth = 1.0f, byte stencil = 0)
{
    public static ClearSettings Solid(Color color) => new(true, true, false, color);
}
