
namespace Inno.Graphics;

/// <summary>
/// Describes clear values for render pass begin.
/// </summary>

public readonly record struct ClearValue(float r, float g, float b, float a, float depth = 1.0f, byte stencil = 0);
