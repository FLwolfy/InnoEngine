
namespace Inno.Graphics;

/// <summary>
/// Describes draw indexed call parameters.
/// </summary>

public readonly record struct DrawIndexedArguments(int indexCount, int instanceCount = 1, int firstIndex = 0, int vertexOffset = 0, int firstInstance = 0);
