using Inno.Core.Mathematics;
using System.Runtime.InteropServices;

namespace Inno.Rendering;

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
