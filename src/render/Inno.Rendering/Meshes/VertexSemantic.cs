using Inno.Core.Mathematics;
using System.Runtime.InteropServices;

namespace Inno.Rendering;

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
