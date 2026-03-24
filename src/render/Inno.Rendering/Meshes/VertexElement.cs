namespace Inno.Rendering;

/// <summary>
/// Defines a single vertex layout element.
/// </summary>
public readonly record struct VertexElement(VertexSemantic semantic, int semanticIndex, int offset, int sizeInBytes);
