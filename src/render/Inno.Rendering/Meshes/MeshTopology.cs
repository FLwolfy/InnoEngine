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
