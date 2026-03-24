using Inno.Core.Mathematics;

namespace Inno.Rendering;

/// <summary>
/// Represents a viewport rectangle in pixels.
/// </summary>
public readonly record struct Viewport(int x, int y, int width, int height);
