using Inno.Core.Mathematics;

namespace Inno.Rendering;

internal static class BuiltinMeshFactory
{
    public static Mesh CreateFullscreenQuad()
    {
        var vertices = new[]
        {
            new StandardVertex { position = new Vector3(-1f, -1f, 0f), normal = Vector3.BACK, tangent = new Vector4(1, 0, 0, 1), texCoord0 = new Vector2(0, 1), color = new Vector4(1, 1, 1, 1) },
            new StandardVertex { position = new Vector3(+1f, -1f, 0f), normal = Vector3.BACK, tangent = new Vector4(1, 0, 0, 1), texCoord0 = new Vector2(1, 1), color = new Vector4(1, 1, 1, 1) },
            new StandardVertex { position = new Vector3(+1f, +1f, 0f), normal = Vector3.BACK, tangent = new Vector4(1, 0, 0, 1), texCoord0 = new Vector2(1, 0), color = new Vector4(1, 1, 1, 1) },
            new StandardVertex { position = new Vector3(-1f, +1f, 0f), normal = Vector3.BACK, tangent = new Vector4(1, 0, 0, 1), texCoord0 = new Vector2(0, 0), color = new Vector4(1, 1, 1, 1) }
        };

        uint[] indices = [0, 1, 2, 2, 3, 0];
        return new MeshBuilder()
            .SetVertices<StandardVertex>(vertices)
            .SetIndices(indices)
            .AddSurface(new MeshSurface(0, indices.Length, 0, MeshTopology.Triangles))
            .Build("BuiltinFullscreenQuad");
    }

    public static Mesh CreateUnitCube()
    {
        const float s = 1f;
        var vertices = new[]
        {
            new StandardVertex { position = new Vector3(-s, -s, +s), normal = Vector3.BACK, tangent = new Vector4(1, 0, 0, 1), texCoord0 = new Vector2(0, 1), color = new Vector4(1, 1, 1, 1) },
            new StandardVertex { position = new Vector3(+s, -s, +s), normal = Vector3.BACK, tangent = new Vector4(1, 0, 0, 1), texCoord0 = new Vector2(1, 1), color = new Vector4(1, 1, 1, 1) },
            new StandardVertex { position = new Vector3(+s, +s, +s), normal = Vector3.BACK, tangent = new Vector4(1, 0, 0, 1), texCoord0 = new Vector2(1, 0), color = new Vector4(1, 1, 1, 1) },
            new StandardVertex { position = new Vector3(-s, +s, +s), normal = Vector3.BACK, tangent = new Vector4(1, 0, 0, 1), texCoord0 = new Vector2(0, 0), color = new Vector4(1, 1, 1, 1) },
            new StandardVertex { position = new Vector3(-s, -s, -s), normal = Vector3.FORWARD, tangent = new Vector4(-1, 0, 0, 1), texCoord0 = new Vector2(1, 1), color = new Vector4(1, 1, 1, 1) },
            new StandardVertex { position = new Vector3(+s, -s, -s), normal = Vector3.FORWARD, tangent = new Vector4(-1, 0, 0, 1), texCoord0 = new Vector2(0, 1), color = new Vector4(1, 1, 1, 1) },
            new StandardVertex { position = new Vector3(+s, +s, -s), normal = Vector3.FORWARD, tangent = new Vector4(-1, 0, 0, 1), texCoord0 = new Vector2(0, 0), color = new Vector4(1, 1, 1, 1) },
            new StandardVertex { position = new Vector3(-s, +s, -s), normal = Vector3.FORWARD, tangent = new Vector4(-1, 0, 0, 1), texCoord0 = new Vector2(1, 0), color = new Vector4(1, 1, 1, 1) }
        };

        uint[] indices =
        [
            0, 1, 2, 2, 3, 0,
            1, 5, 6, 6, 2, 1,
            5, 4, 7, 7, 6, 5,
            4, 0, 3, 3, 7, 4,
            3, 2, 6, 6, 7, 3,
            4, 5, 1, 1, 0, 4
        ];

        return new MeshBuilder()
            .SetVertices<StandardVertex>(vertices)
            .SetIndices(indices)
            .AddSurface(new MeshSurface(0, indices.Length, 0, MeshTopology.Triangles))
            .Build("BuiltinUnitCube");
    }
}
