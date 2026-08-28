using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Inno.Core.Mathematics;

namespace Inno.Rendering.Assets;

internal static partial class MeshSourceParser
{
    private const uint C_GLB_MAGIC = 0x46546c67;
    private const uint C_JSON_CHUNK = 0x4e4f534a;
    private const uint C_BIN_CHUNK = 0x004e4942;

    internal static MeshData ParseGltf(
        string sourcePath,
        ReadOnlySpan<byte> source,
        bool isBinary,
        Func<string, byte[]> dependencyReader,
        Action<string> dependencySink)
    {
        ArgumentNullException.ThrowIfNull(dependencyReader);
        ArgumentNullException.ThrowIfNull(dependencySink);
        byte[] jsonBytes;
        byte[]? binaryChunk = null;
        if (isBinary)
        {
            (jsonBytes, binaryChunk) = ReadGlb(sourcePath, source);
        }
        else
        {
            jsonBytes = source.ToArray();
        }

        using JsonDocument document = JsonDocument.Parse(jsonBytes);
        JsonElement root = RenderingJson.RequireObject(document.RootElement, "$gltf");
        byte[][] buffers = ReadBuffers(
            sourcePath,
            root,
            binaryChunk,
            dependencyReader,
            dependencySink);
        BufferView[] bufferViews = ReadBufferViews(root, buffers);
        Accessor[] accessors = ReadAccessors(root, bufferViews);
        return ReadMeshes(sourcePath, root, accessors);
    }

    private static (byte[] json, byte[]? binary) ReadGlb(string path, ReadOnlySpan<byte> source)
    {
        if (source.Length < 20 || BinaryPrimitives.ReadUInt32LittleEndian(source) != C_GLB_MAGIC)
        {
            throw new RenderingAssetFormatException(path, "Invalid GLB header.");
        }

        uint gltfVersion = BinaryPrimitives.ReadUInt32LittleEndian(source.Slice(4, 4));
        uint declaredLength = BinaryPrimitives.ReadUInt32LittleEndian(source.Slice(8, 4));
        if (gltfVersion != 2 || declaredLength != source.Length)
        {
            throw new RenderingAssetFormatException(path, "Only complete glTF 2 GLB containers are supported.");
        }

        byte[]? json = null;
        byte[]? binary = null;
        int offset = 12;
        while (offset + 8 <= source.Length)
        {
            int chunkLength = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(source.Slice(offset, 4)));
            uint chunkType = BinaryPrimitives.ReadUInt32LittleEndian(source.Slice(offset + 4, 4));
            offset += 8;
            if (chunkLength < 0 || offset + chunkLength > source.Length)
            {
                throw new RenderingAssetFormatException(path, "GLB chunk exceeds the container bounds.");
            }

            if (chunkType == C_JSON_CHUNK && json is null)
            {
                json = source.Slice(offset, chunkLength).ToArray();
            }
            else if (chunkType == C_BIN_CHUNK && binary is null)
            {
                binary = source.Slice(offset, chunkLength).ToArray();
            }

            offset += chunkLength;
        }

        return (json ?? throw new RenderingAssetFormatException(path, "GLB JSON chunk is missing."), binary);
    }

    private static byte[][] ReadBuffers(
        string sourcePath,
        JsonElement root,
        byte[]? binaryChunk,
        Func<string, byte[]> dependencyReader,
        Action<string> dependencySink)
    {
        if (!root.TryGetProperty("buffers", out JsonElement bufferArray))
        {
            throw new RenderingAssetFormatException(sourcePath, "glTF buffers are missing.");
        }

        RenderingJson.RequireKind(bufferArray, JsonValueKind.Array, "$gltf.buffers");
        string directory = GetProjectDirectory(sourcePath);
        var buffers = new List<byte[]>();
        int index = 0;
        foreach (JsonElement buffer in bufferArray.EnumerateArray())
        {
            RenderingJson.RequireObject(buffer, $"$gltf.buffers[{index}]");
            int byteLength = RenderingJson.RequireInt32(
                buffer.GetProperty("byteLength"),
                $"$gltf.buffers[{index}].byteLength");
            byte[] bytes;
            if (buffer.TryGetProperty("uri", out JsonElement uriElement))
            {
                string uri = RenderingJson.RequireString(uriElement, $"$gltf.buffers[{index}].uri");
                if (uri.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                {
                    int separator = uri.IndexOf(',');
                    if (separator < 0 || !uri[..separator].EndsWith(";base64", StringComparison.OrdinalIgnoreCase))
                    {
                        throw new RenderingAssetFormatException(
                            $"$gltf.buffers[{index}].uri",
                            "Only base64 data URIs are supported.");
                    }

                    bytes = Convert.FromBase64String(uri[(separator + 1)..]);
                }
                else
                {
                    string dependency = NormalizeProjectPath(
                        CombineProjectPath(directory, Uri.UnescapeDataString(uri)),
                        $"$gltf.buffers[{index}].uri");
                    dependencySink(dependency);
                    bytes = dependencyReader(dependency);
                }
            }
            else if (index == 0 && binaryChunk is not null)
            {
                bytes = binaryChunk;
            }
            else
            {
                throw new RenderingAssetFormatException(
                    $"$gltf.buffers[{index}]",
                    "A URI or matching GLB binary chunk is required.");
            }

            if (byteLength < 0 || bytes.Length < byteLength)
            {
                throw new RenderingAssetFormatException(
                    $"$gltf.buffers[{index}].byteLength",
                    "Buffer content is shorter than its declared byte length.");
            }

            buffers.Add(bytes);
            index++;
        }

        return [.. buffers];
    }

    private static BufferView[] ReadBufferViews(JsonElement root, IReadOnlyList<byte[]> buffers)
    {
        if (!root.TryGetProperty("bufferViews", out JsonElement viewArray))
        {
            return [];
        }

        RenderingJson.RequireKind(viewArray, JsonValueKind.Array, "$gltf.bufferViews");
        var result = new List<BufferView>();
        int index = 0;
        foreach (JsonElement view in viewArray.EnumerateArray())
        {
            int bufferIndex = RenderingJson.RequireInt32(
                view.GetProperty("buffer"),
                $"$gltf.bufferViews[{index}].buffer");
            int offset = view.TryGetProperty("byteOffset", out JsonElement offsetElement)
                ? RenderingJson.RequireInt32(offsetElement, $"$gltf.bufferViews[{index}].byteOffset")
                : 0;
            int length = RenderingJson.RequireInt32(
                view.GetProperty("byteLength"),
                $"$gltf.bufferViews[{index}].byteLength");
            int stride = view.TryGetProperty("byteStride", out JsonElement strideElement)
                ? RenderingJson.RequireInt32(strideElement, $"$gltf.bufferViews[{index}].byteStride")
                : 0;
            if (bufferIndex < 0 || bufferIndex >= buffers.Count
                || offset < 0 || length < 0 || offset + length > buffers[bufferIndex].Length
                || stride < 0)
            {
                throw new RenderingAssetFormatException(
                    $"$gltf.bufferViews[{index}]",
                    "Buffer view bounds are invalid.");
            }

            result.Add(new BufferView(buffers[bufferIndex], offset, length, stride));
            index++;
        }

        return [.. result];
    }

    private static Accessor[] ReadAccessors(JsonElement root, IReadOnlyList<BufferView> views)
    {
        if (!root.TryGetProperty("accessors", out JsonElement accessorArray))
        {
            return [];
        }

        RenderingJson.RequireKind(accessorArray, JsonValueKind.Array, "$gltf.accessors");
        var result = new List<Accessor>();
        int index = 0;
        foreach (JsonElement accessor in accessorArray.EnumerateArray())
        {
            if (accessor.TryGetProperty("sparse", out _))
            {
                throw new RenderingAssetFormatException(
                    $"$gltf.accessors[{index}].sparse",
                    "Sparse accessors are not supported by the first mesh importer.");
            }

            int viewIndex = RenderingJson.RequireInt32(
                accessor.GetProperty("bufferView"),
                $"$gltf.accessors[{index}].bufferView");
            int offset = accessor.TryGetProperty("byteOffset", out JsonElement offsetElement)
                ? RenderingJson.RequireInt32(offsetElement, $"$gltf.accessors[{index}].byteOffset")
                : 0;
            int componentType = RenderingJson.RequireInt32(
                accessor.GetProperty("componentType"),
                $"$gltf.accessors[{index}].componentType");
            int count = RenderingJson.RequireInt32(
                accessor.GetProperty("count"),
                $"$gltf.accessors[{index}].count");
            string type = RenderingJson.RequireString(
                accessor.GetProperty("type"),
                $"$gltf.accessors[{index}].type");
            bool normalized = accessor.TryGetProperty("normalized", out JsonElement normalizedElement)
                && RenderingJson.RequireBoolean(normalizedElement, $"$gltf.accessors[{index}].normalized");
            if (viewIndex < 0 || viewIndex >= views.Count || offset < 0 || count <= 0)
            {
                throw new RenderingAssetFormatException(
                    $"$gltf.accessors[{index}]",
                    "Accessor bounds are invalid.");
            }

            result.Add(new Accessor(views[viewIndex], offset, componentType, count, type, normalized));
            index++;
        }

        return [.. result];
    }

    private static MeshData ReadMeshes(
        string sourcePath,
        JsonElement root,
        IReadOnlyList<Accessor> accessors)
    {
        if (!root.TryGetProperty("meshes", out JsonElement meshes))
        {
            throw new RenderingAssetFormatException(sourcePath, "glTF contains no meshes.");
        }

        RenderingJson.RequireKind(meshes, JsonValueKind.Array, "$gltf.meshes");
        var vertices = new List<MutableVertex>();
        var indices = new List<uint>();
        var subMeshes = new List<MeshSubMesh>();
        foreach (JsonElement mesh in meshes.EnumerateArray())
        {
            JsonElement primitives = mesh.GetProperty("primitives");
            RenderingJson.RequireKind(primitives, JsonValueKind.Array, "$gltf.meshes[].primitives");
            foreach (JsonElement primitive in primitives.EnumerateArray())
            {
                int mode = primitive.TryGetProperty("mode", out JsonElement modeElement)
                    ? RenderingJson.RequireInt32(modeElement, "$gltf.meshes[].primitives[].mode")
                    : 4;
                if (mode != 4)
                {
                    throw new RenderingAssetFormatException(
                        "$gltf.meshes[].primitives[].mode",
                        "Only triangle-list primitives are supported.");
                }

                JsonElement attributes = primitive.GetProperty("attributes");
                int positionAccessor = RenderingJson.RequireInt32(
                    attributes.GetProperty("POSITION"),
                    "$gltf.meshes[].primitives[].attributes.POSITION");
                float[][] positions = ReadFloatAccessor(accessors, positionAccessor, 3, "POSITION");
                float[][]? normals = TryReadAttribute(attributes, "NORMAL", accessors, 3);
                float[][]? textureCoordinates = TryReadAttribute(attributes, "TEXCOORD_0", accessors, 2);
                float[][]? tangents = TryReadAttribute(attributes, "TANGENT", accessors, 4);
                ValidateAttributeCount(positions.Length, normals, "NORMAL");
                ValidateAttributeCount(positions.Length, textureCoordinates, "TEXCOORD_0");
                ValidateAttributeCount(positions.Length, tangents, "TANGENT");

                int baseVertex = vertices.Count;
                for (int index = 0; index < positions.Length; index++)
                {
                    float[] position = positions[index];
                    float[]? normal = normals?[index];
                    float[]? uv = textureCoordinates?[index];
                    float[]? tangent = tangents?[index];
                    vertices.Add(new MutableVertex
                    {
                        position = new Vector3(position[0], position[1], -position[2]),
                        normal = normal is null
                            ? default
                            : new Vector3(normal[0], normal[1], -normal[2]),
                        hasNormal = normal is not null,
                        textureCoordinate = uv is null ? default : new Vector2(uv[0], uv[1]),
                        tangent = tangent is null
                            ? default
                            : new Vector3(tangent[0], tangent[1], -tangent[2]),
                        tangentW = tangent is null ? 1f : -tangent[3],
                        hasTangent = tangent is not null
                    });
                }

                uint[] localIndices = primitive.TryGetProperty("indices", out JsonElement indexElement)
                    ? ReadIndexAccessor(
                        accessors,
                        RenderingJson.RequireInt32(indexElement, "$gltf.meshes[].primitives[].indices"))
                    : Enumerable.Range(0, positions.Length).Select(static value => checked((uint)value)).ToArray();
                if (localIndices.Length % 3 != 0)
                {
                    throw new RenderingAssetFormatException(
                        "$gltf.meshes[].primitives[].indices",
                        "Triangle index count must be divisible by three.");
                }

                int firstIndex = indices.Count;
                for (int index = 0; index < localIndices.Length; index += 3)
                {
                    ValidateLocalIndex(localIndices[index], positions.Length);
                    ValidateLocalIndex(localIndices[index + 1], positions.Length);
                    ValidateLocalIndex(localIndices[index + 2], positions.Length);
                    indices.Add(checked((uint)baseVertex + localIndices[index]));
                    indices.Add(checked((uint)baseVertex + localIndices[index + 2]));
                    indices.Add(checked((uint)baseVertex + localIndices[index + 1]));
                }

                subMeshes.Add(new MeshSubMesh(firstIndex, indices.Count - firstIndex));
            }
        }

        if (vertices.Count == 0 || indices.Count == 0)
        {
            throw new RenderingAssetFormatException(sourcePath, "glTF contains no triangle geometry.");
        }

        GenerateMissingNormalsAndTangents(vertices, indices);
        MeshVertex[] normalizedVertices = vertices.Select(static value => new MeshVertex(
            value.position,
            Vector3.NormalizeSafe(value.normal),
            ToTangent(value.tangent, value.tangentW),
            value.textureCoordinate)).ToArray();
        return new MeshData(normalizedVertices, [.. indices], [.. subMeshes]);
    }

    private static float[][]? TryReadAttribute(
        JsonElement attributes,
        string name,
        IReadOnlyList<Accessor> accessors,
        int components)
        => attributes.TryGetProperty(name, out JsonElement accessor)
            ? ReadFloatAccessor(
                accessors,
                RenderingJson.RequireInt32(accessor, $"$gltf.attributes.{name}"),
                components,
                name)
            : null;

    private static float[][] ReadFloatAccessor(
        IReadOnlyList<Accessor> accessors,
        int index,
        int expectedComponents,
        string semantic)
    {
        Accessor accessor = GetAccessor(accessors, index, semantic);
        if (ComponentCount(accessor.type) != expectedComponents)
        {
            throw new RenderingAssetFormatException(
                $"$gltf.accessors[{index}]",
                $"Attribute '{semantic}' must contain {expectedComponents} components.");
        }

        int componentSize = ComponentSize(accessor.componentType);
        int elementSize = componentSize * expectedComponents;
        int stride = accessor.view.stride == 0 ? elementSize : accessor.view.stride;
        ValidateAccessorBounds(accessor, elementSize, stride, index);
        var values = new float[accessor.count][];
        for (int element = 0; element < accessor.count; element++)
        {
            values[element] = new float[expectedComponents];
            int offset = accessor.view.offset + accessor.offset + element * stride;
            for (int component = 0; component < expectedComponents; component++)
            {
                values[element][component] = ReadComponent(
                    accessor.view.buffer,
                    offset + component * componentSize,
                    accessor.componentType,
                    accessor.normalized);
            }
        }

        return values;
    }

    private static uint[] ReadIndexAccessor(IReadOnlyList<Accessor> accessors, int index)
    {
        Accessor accessor = GetAccessor(accessors, index, "indices");
        if (!string.Equals(accessor.type, "SCALAR", StringComparison.Ordinal)
            || accessor.componentType is not (5121 or 5123 or 5125))
        {
            throw new RenderingAssetFormatException(
                $"$gltf.accessors[{index}]",
                "Indices require an unsigned SCALAR accessor.");
        }

        int elementSize = ComponentSize(accessor.componentType);
        int stride = accessor.view.stride == 0 ? elementSize : accessor.view.stride;
        ValidateAccessorBounds(accessor, elementSize, stride, index);
        var result = new uint[accessor.count];
        for (int element = 0; element < result.Length; element++)
        {
            int offset = accessor.view.offset + accessor.offset + element * stride;
            result[element] = accessor.componentType switch
            {
                5121 => accessor.view.buffer[offset],
                5123 => BinaryPrimitives.ReadUInt16LittleEndian(accessor.view.buffer.AsSpan(offset, 2)),
                5125 => BinaryPrimitives.ReadUInt32LittleEndian(accessor.view.buffer.AsSpan(offset, 4)),
                _ => throw new InvalidOperationException()
            };
        }

        return result;
    }

    private static float ReadComponent(byte[] buffer, int offset, int componentType, bool normalized)
        => componentType switch
        {
            5120 => normalized ? Math.Max((sbyte)buffer[offset] / 127f, -1f) : (sbyte)buffer[offset],
            5121 => normalized ? buffer[offset] / 255f : buffer[offset],
            5122 => normalized
                ? Math.Max(BinaryPrimitives.ReadInt16LittleEndian(buffer.AsSpan(offset, 2)) / 32767f, -1f)
                : BinaryPrimitives.ReadInt16LittleEndian(buffer.AsSpan(offset, 2)),
            5123 => normalized
                ? BinaryPrimitives.ReadUInt16LittleEndian(buffer.AsSpan(offset, 2)) / 65535f
                : BinaryPrimitives.ReadUInt16LittleEndian(buffer.AsSpan(offset, 2)),
            5125 => normalized
                ? BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(offset, 4)) / (float)uint.MaxValue
                : BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(offset, 4)),
            5126 => BitConverter.Int32BitsToSingle(
                BinaryPrimitives.ReadInt32LittleEndian(buffer.AsSpan(offset, 4))),
            _ => throw new RenderingAssetFormatException("$gltf.accessor", $"Unsupported component type '{componentType}'.")
        };

    private static void ValidateAccessorBounds(Accessor accessor, int elementSize, int stride, int index)
    {
        long end = (long)accessor.offset + (long)(accessor.count - 1) * stride + elementSize;
        if (stride < elementSize || accessor.offset < 0 || end > accessor.view.length)
        {
            throw new RenderingAssetFormatException(
                $"$gltf.accessors[{index}]",
                "Accessor exceeds its buffer view or uses an invalid stride.");
        }
    }

    private static Accessor GetAccessor(IReadOnlyList<Accessor> accessors, int index, string semantic)
        => index >= 0 && index < accessors.Count
            ? accessors[index]
            : throw new RenderingAssetFormatException(
                $"$gltf.{semantic}",
                $"Accessor index '{index}' is out of range.");

    private static int ComponentCount(string type)
        => type switch
        {
            "SCALAR" => 1,
            "VEC2" => 2,
            "VEC3" => 3,
            "VEC4" => 4,
            _ => throw new RenderingAssetFormatException("$gltf.accessor.type", $"Unsupported accessor type '{type}'.")
        };

    private static int ComponentSize(int type)
        => type switch
        {
            5120 or 5121 => 1,
            5122 or 5123 => 2,
            5125 or 5126 => 4,
            _ => throw new RenderingAssetFormatException("$gltf.accessor.componentType", $"Unsupported component type '{type}'.")
        };

    private static void ValidateAttributeCount(int expected, float[][]? values, string semantic)
    {
        if (values is not null && values.Length != expected)
        {
            throw new RenderingAssetFormatException(
                $"$gltf.attributes.{semantic}",
                "Attribute count does not match POSITION.");
        }
    }

    private static void ValidateLocalIndex(uint index, int vertexCount)
    {
        if (index >= vertexCount)
        {
            throw new RenderingAssetFormatException("$gltf.indices", $"Vertex index '{index}' is out of range.");
        }
    }

    private static string NormalizeProjectPath(string value, string path)
    {
        string normalized = value.Replace('\\', '/').TrimStart('/');
        if (string.IsNullOrWhiteSpace(normalized)
            || normalized.Split('/', StringSplitOptions.RemoveEmptyEntries).Contains("..", StringComparer.Ordinal))
        {
            throw new RenderingAssetFormatException(path, "A project-relative URI without '..' is required.");
        }

        return normalized;
    }

    private static string GetProjectDirectory(string sourcePath)
    {
        int separator = sourcePath.LastIndexOf('/');
        return separator < 0 ? string.Empty : sourcePath[..separator];
    }

    private static string CombineProjectPath(string directory, string relative)
        => string.IsNullOrEmpty(directory) ? relative : $"{directory}/{relative}";

    private readonly record struct BufferView(byte[] buffer, int offset, int length, int stride);
    private readonly record struct Accessor(
        BufferView view,
        int offset,
        int componentType,
        int count,
        string type,
        bool normalized);
}
