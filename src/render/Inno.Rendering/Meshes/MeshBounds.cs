using Inno.Core.Mathematics;
using System.Runtime.InteropServices;

namespace Inno.Rendering;

/// <summary>
/// Defines an axis-aligned mesh bounds volume.
/// </summary>
public readonly record struct MeshBounds(Vector3 center, Vector3 extents);
