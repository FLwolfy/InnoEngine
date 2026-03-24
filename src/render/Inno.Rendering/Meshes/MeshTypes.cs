using Inno.Core.Mathematics;
using System.Runtime.InteropServices;

namespace Inno.Rendering;

/// <summary>
/// Defines mesh primitive topology.
/// </summary>
public enum MeshTopology
{
    Triangles = 0,
    TriangleStrip,
    Lines,
    LineStrip,
    Points
}

/// <summary>
/// Defines standard vertex semantics.
/// </summary>
public enum VertexSemantic
{
    Position = 0,
    Normal,
    Tangent,
    Bitangent,
    Color0,
    TexCoord0,
    TexCoord1,
    TexCoord2,
    TexCoord3,
    BlendIndices,
    BlendWeights
}

/// <summary>
/// Defines an axis-aligned mesh bounds volume.
/// </summary>
public readonly record struct MeshBounds(Vector3 center, Vector3 extents);

/// <summary>
/// Defines a sub-range of indexed geometry.
/// </summary>
public readonly record struct MeshSurface(int indexStart, int indexCount, int materialSlot, MeshTopology topology);

/// <summary>
/// Defines a single vertex layout element.
/// </summary>
public readonly record struct VertexElement(VertexSemantic semantic, int semanticIndex, int offset, int sizeInBytes);

/// <summary>
/// Defines a commonly used PBR-compatible vertex shape.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct StandardVertex
{
    public Vector3 position;

    public Vector3 normal;

    public Vector4 tangent;

    public Vector2 texCoord0;

    public Vector4 color;
}
