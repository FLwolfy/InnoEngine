using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Inno.Core.Mathematics;

namespace Inno.Rendering.Assets;

internal static partial class MeshSourceParser
{
    internal static MeshData ParseObj(string sourcePath, string text)
    {
        var positions = new List<Vector3>();
        var normals = new List<Vector3>();
        var textureCoordinates = new List<Vector2>();
        var vertices = new List<MutableVertex>();
        var indices = new List<uint>();
        var vertexMap = new Dictionary<ObjVertexKey, uint>();
        var subMeshes = new List<MeshSubMesh>();
        int subMeshStart = 0;

        string[] lines = text.Split('\n');
        for (int lineIndex = 0; lineIndex < lines.Length; lineIndex++)
        {
            string line = lines[lineIndex].Trim();
            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }

            string[] parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            switch (parts[0])
            {
                case "v":
                    RequirePartCount(parts, 4, sourcePath, lineIndex);
                    positions.Add(new Vector3(
                        ParseFloat(parts[1], sourcePath, lineIndex),
                        ParseFloat(parts[2], sourcePath, lineIndex),
                        ParseFloat(parts[3], sourcePath, lineIndex)));
                    break;
                case "vn":
                    RequirePartCount(parts, 4, sourcePath, lineIndex);
                    normals.Add(Vector3.NormalizeSafe(new Vector3(
                        ParseFloat(parts[1], sourcePath, lineIndex),
                        ParseFloat(parts[2], sourcePath, lineIndex),
                        ParseFloat(parts[3], sourcePath, lineIndex))));
                    break;
                case "vt":
                    RequirePartCount(parts, 3, sourcePath, lineIndex);
                    textureCoordinates.Add(new Vector2(
                        ParseFloat(parts[1], sourcePath, lineIndex),
                        ParseFloat(parts[2], sourcePath, lineIndex)));
                    break;
                case "f":
                    if (parts.Length < 4)
                    {
                        throw Error(sourcePath, lineIndex, "A face requires at least three vertices.");
                    }

                    uint[] polygon = parts.Skip(1)
                        .Select(value => ResolveObjVertex(
                            value,
                            sourcePath,
                            lineIndex,
                            positions,
                            normals,
                            textureCoordinates,
                            vertices,
                            vertexMap))
                        .ToArray();
                    for (int corner = 1; corner < polygon.Length - 1; corner++)
                    {
                        indices.Add(polygon[0]);
                        indices.Add(polygon[corner]);
                        indices.Add(polygon[corner + 1]);
                    }
                    break;
                case "g":
                case "o":
                case "usemtl":
                    CloseSubMesh(indices.Count, ref subMeshStart, subMeshes);
                    break;
            }
        }

        CloseSubMesh(indices.Count, ref subMeshStart, subMeshes);
        if (vertices.Count == 0 || indices.Count == 0)
        {
            throw new RenderingAssetFormatException(sourcePath, "OBJ contains no triangle geometry.");
        }

        GenerateMissingNormalsAndTangents(vertices, indices);
        MeshVertex[] resultVertices = vertices.Select(static value => new MeshVertex(
            value.position,
            Vector3.NormalizeSafe(value.normal),
            ToTangent(value.tangent, value.tangentW),
            value.textureCoordinate)).ToArray();
        return new MeshData(resultVertices, [.. indices], [.. subMeshes]);
    }

    internal static void GenerateMissingNormalsAndTangents(
        List<MutableVertex> vertices,
        IReadOnlyList<uint> indices)
    {
        for (int index = 0; index < indices.Count; index += 3)
        {
            int i0 = checked((int)indices[index]);
            int i1 = checked((int)indices[index + 1]);
            int i2 = checked((int)indices[index + 2]);
            MutableVertex v0 = vertices[i0];
            MutableVertex v1 = vertices[i1];
            MutableVertex v2 = vertices[i2];
            Vector3 edge1 = v1.position - v0.position;
            Vector3 edge2 = v2.position - v0.position;
            Vector3 faceNormal = Vector3.Cross(edge1, edge2);
            if (!v0.hasNormal) v0.normal += faceNormal;
            if (!v1.hasNormal) v1.normal += faceNormal;
            if (!v2.hasNormal) v2.normal += faceNormal;

            Vector2 uv1 = v1.textureCoordinate - v0.textureCoordinate;
            Vector2 uv2 = v2.textureCoordinate - v0.textureCoordinate;
            float determinant = uv1.x * uv2.y - uv1.y * uv2.x;
            if (MathF.Abs(determinant) > 1e-8f)
            {
                float inverse = 1f / determinant;
                Vector3 tangent = (edge1 * uv2.y - edge2 * uv1.y) * inverse;
                if (!v0.hasTangent) v0.tangent += tangent;
                if (!v1.hasTangent) v1.tangent += tangent;
                if (!v2.hasTangent) v2.tangent += tangent;
            }
        }
    }

    private static uint ResolveObjVertex(
        string token,
        string sourcePath,
        int lineIndex,
        IReadOnlyList<Vector3> positions,
        IReadOnlyList<Vector3> normals,
        IReadOnlyList<Vector2> textureCoordinates,
        List<MutableVertex> vertices,
        Dictionary<ObjVertexKey, uint> vertexMap)
    {
        string[] values = token.Split('/');
        int position = ResolveIndex(values[0], positions.Count, sourcePath, lineIndex, "position");
        int texture = values.Length > 1 && values[1].Length != 0
            ? ResolveIndex(values[1], textureCoordinates.Count, sourcePath, lineIndex, "texture coordinate")
            : -1;
        int normal = values.Length > 2 && values[2].Length != 0
            ? ResolveIndex(values[2], normals.Count, sourcePath, lineIndex, "normal")
            : -1;
        var key = new ObjVertexKey(position, texture, normal);
        if (vertexMap.TryGetValue(key, out uint existing))
        {
            return existing;
        }

        uint created = checked((uint)vertices.Count);
        vertices.Add(new MutableVertex
        {
            position = positions[position],
            normal = normal >= 0 ? normals[normal] : default,
            hasNormal = normal >= 0,
            textureCoordinate = texture >= 0 ? textureCoordinates[texture] : default
        });
        vertexMap.Add(key, created);
        return created;
    }

    private static int ResolveIndex(
        string value,
        int count,
        string sourcePath,
        int lineIndex,
        string kind)
    {
        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)
            || parsed == 0)
        {
            throw Error(sourcePath, lineIndex, $"Invalid OBJ {kind} index '{value}'.");
        }

        int resolved = parsed > 0 ? parsed - 1 : count + parsed;
        if (resolved < 0 || resolved >= count)
        {
            throw Error(sourcePath, lineIndex, $"OBJ {kind} index '{value}' is out of range.");
        }

        return resolved;
    }

    private static float ParseFloat(string value, string sourcePath, int lineIndex)
        => float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float result)
            && float.IsFinite(result)
                ? result
                : throw Error(sourcePath, lineIndex, $"Invalid finite float '{value}'.");

    private static void RequirePartCount(
        IReadOnlyList<string> values,
        int count,
        string sourcePath,
        int lineIndex)
    {
        if (values.Count < count)
        {
            throw Error(sourcePath, lineIndex, "OBJ record has too few values.");
        }
    }

    private static void CloseSubMesh(
        int currentIndexCount,
        ref int subMeshStart,
        List<MeshSubMesh> subMeshes)
    {
        if (currentIndexCount > subMeshStart)
        {
            subMeshes.Add(new MeshSubMesh(subMeshStart, currentIndexCount - subMeshStart));
            subMeshStart = currentIndexCount;
        }
    }

    internal static Vector4 ToTangent(Vector3 value, float handedness)
    {
        Vector3 normalized = Vector3.NormalizeSafe(value);
        if (normalized.LengthSquared() <= 1e-8f)
        {
            normalized = Vector3.RIGHT;
        }

        return new Vector4(normalized.x, normalized.y, normalized.z, handedness < 0f ? -1f : 1f);
    }

    private static RenderingAssetFormatException Error(string path, int lineIndex, string message)
        => new($"{path}:{lineIndex + 1}", message);

    private readonly record struct ObjVertexKey(int position, int texture, int normal);

    internal sealed class MutableVertex
    {
        internal Vector3 position;
        internal Vector3 normal;
        internal Vector3 tangent;
        internal Vector2 textureCoordinate;
        internal bool hasNormal;
        internal bool hasTangent;
        internal float tangentW = 1f;
    }
}
