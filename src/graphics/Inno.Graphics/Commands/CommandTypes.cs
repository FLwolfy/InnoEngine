using Inno.Graphics;

namespace Inno.Graphics;

/// <summary>
/// Defines primitive topology.
/// </summary>
public enum GraphicsPrimitiveType
{
    Triangles = 0,
    TriangleStrip,
    Lines,
    LineStrip,
    Points
}

/// <summary>
/// Defines a viewport rectangle.
/// </summary>
public readonly record struct GraphicsViewport(float x, float y, float width, float height, float minDepth = 0.0f, float maxDepth = 1.0f);

/// <summary>
/// Defines a scissor rectangle.
/// </summary>
public readonly record struct GraphicsScissorRect(int x, int y, int width, int height);

/// <summary>
/// Describes draw indexed call parameters.
/// </summary>
public readonly record struct DrawIndexedArguments(int indexCount, int instanceCount = 1, int firstIndex = 0, int vertexOffset = 0, int firstInstance = 0);

/// <summary>
/// Describes clear values for render pass begin.
/// </summary>
public readonly record struct ClearValue(float r, float g, float b, float a, float depth = 1.0f, byte stencil = 0);
