
namespace Inno.Graphics;

/// <summary>
/// Defines a viewport rectangle.
/// </summary>

public readonly record struct GraphicsViewport(float x, float y, float width, float height, float minDepth = 0.0f, float maxDepth = 1.0f);
