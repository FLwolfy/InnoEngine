using Inno.Core.Mathematics;
using System.Runtime.InteropServices;

namespace Inno.Rendering;

/// <summary>
/// Defines a sub-range of indexed geometry.
/// </summary>
public readonly record struct MeshSurface(int indexStart, int indexCount, int materialSlot, MeshTopology topology);
