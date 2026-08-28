using System;
using System.Collections.Generic;
using System.IO;
using Inno.Core.Mathematics;

namespace Inno.Rendering.Assets;

/// <summary>
/// Stores one normalized vertex shared by all rendering backends.
/// </summary>
public readonly record struct MeshVertex
{
    /// <summary>
    /// Creates a normalized mesh vertex.
    /// </summary>
    /// <param name="position">Object-space position.</param>
    /// <param name="normal">Object-space unit normal.</param>
    /// <param name="tangent">Object-space tangent and handedness.</param>
    /// <param name="textureCoordinate">Primary texture coordinate.</param>
    public MeshVertex(Vector3 position, Vector3 normal, Vector4 tangent, Vector2 textureCoordinate)
    {
        this.position = position;
        this.normal = normal;
        this.tangent = tangent;
        this.textureCoordinate = textureCoordinate;
    }

    /// <summary>Gets the object-space position.</summary>
    public Vector3 position { get; }

    /// <summary>Gets the object-space unit normal.</summary>
    public Vector3 normal { get; }

    /// <summary>Gets the object-space tangent and handedness.</summary>
    public Vector4 tangent { get; }

    /// <summary>Gets the primary texture coordinate.</summary>
    public Vector2 textureCoordinate { get; }
}

/// <summary>
/// Identifies a contiguous triangle-index range.
/// </summary>
public readonly record struct MeshSubMesh
{
    /// <summary>
    /// Creates a submesh range.
    /// </summary>
    /// <param name="firstIndex">First index in the shared index buffer.</param>
    /// <param name="indexCount">Number of indices in the range.</param>
    public MeshSubMesh(int firstIndex, int indexCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(firstIndex);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(indexCount);
        this.firstIndex = firstIndex;
        this.indexCount = indexCount;
    }

    /// <summary>Gets the first index in the shared index buffer.</summary>
    public int firstIndex { get; }

    /// <summary>Gets the number of indices in the range.</summary>
    public int indexCount { get; }
}

/// <summary>
/// Contains normalized CPU mesh data ready for backend upload.
/// </summary>
public sealed class MeshData
{
    /// <summary>
    /// Creates normalized mesh data.
    /// </summary>
    /// <param name="vertices">Vertex stream.</param>
    /// <param name="indices">Triangle index stream.</param>
    /// <param name="subMeshes">Contiguous submesh ranges.</param>
    public MeshData(MeshVertex[] vertices, uint[] indices, MeshSubMesh[] subMeshes)
    {
        ArgumentNullException.ThrowIfNull(vertices);
        ArgumentNullException.ThrowIfNull(indices);
        ArgumentNullException.ThrowIfNull(subMeshes);
        this.vertices = (MeshVertex[])vertices.Clone();
        this.indices = (uint[])indices.Clone();
        this.subMeshes = (MeshSubMesh[])subMeshes.Clone();
    }

    /// <summary>Gets the normalized vertex stream.</summary>
    public IReadOnlyList<MeshVertex> vertices { get; }

    /// <summary>Gets the triangle index stream.</summary>
    public IReadOnlyList<uint> indices { get; }

    /// <summary>Gets contiguous submesh ranges.</summary>
    public IReadOnlyList<MeshSubMesh> subMeshes { get; }
}

/// <summary>
/// Decodes normalized CPU geometry committed by mesh importers.
/// </summary>
public static class MeshAssetRuntime
{
    /// <summary>
    /// Decodes the current committed mesh payload.
    /// </summary>
    /// <param name="mesh">Imported mesh asset.</param>
    /// <returns>Normalized vertices, triangle indices and submeshes.</returns>
    /// <exception cref="InvalidDataException">Thrown when the payload is missing or corrupt.</exception>
    public static MeshData GetMeshData(MeshAsset mesh)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        return MeshArtifactCodec.Decode(mesh.runtimePayload.Span);
    }
}

internal static class MeshArtifactCodec
{
    private const ulong C_MAGIC = 0x4853454D4F4E4E49;

    internal static byte[] Encode(MeshData data)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(C_MAGIC);
        writer.Write(data.vertices.Count);
        writer.Write(data.indices.Count);
        writer.Write(data.subMeshes.Count);
        foreach (MeshVertex vertex in data.vertices)
        {
            writer.Write(vertex.position.x);
            writer.Write(vertex.position.y);
            writer.Write(vertex.position.z);
            writer.Write(vertex.normal.x);
            writer.Write(vertex.normal.y);
            writer.Write(vertex.normal.z);
            writer.Write(vertex.tangent.x);
            writer.Write(vertex.tangent.y);
            writer.Write(vertex.tangent.z);
            writer.Write(vertex.tangent.w);
            writer.Write(vertex.textureCoordinate.x);
            writer.Write(vertex.textureCoordinate.y);
        }

        foreach (uint index in data.indices)
        {
            writer.Write(index);
        }

        foreach (MeshSubMesh subMesh in data.subMeshes)
        {
            writer.Write(subMesh.firstIndex);
            writer.Write(subMesh.indexCount);
        }

        return stream.ToArray();
    }

    internal static MeshData Decode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.IsEmpty)
        {
            throw new InvalidDataException("Mesh artifact payload is empty.");
        }

        using var stream = new MemoryStream(bytes.ToArray(), writable: false);
        using var reader = new BinaryReader(stream);
        if (reader.ReadUInt64() != C_MAGIC)
        {
            throw new InvalidDataException("Mesh artifact magic is invalid.");
        }

        int vertexCount = RequireCount(reader.ReadInt32(), "vertex");
        int indexCount = RequireCount(reader.ReadInt32(), "index");
        int subMeshCount = RequireCount(reader.ReadInt32(), "submesh");
        var vertices = new MeshVertex[vertexCount];
        var indices = new uint[indexCount];
        var subMeshes = new MeshSubMesh[subMeshCount];
        try
        {
            for (int index = 0; index < vertices.Length; index++)
            {
                vertices[index] = new MeshVertex(
                    new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle()),
                    new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle()),
                    new Vector4(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle()),
                    new Vector2(reader.ReadSingle(), reader.ReadSingle()));
            }

            for (int index = 0; index < indices.Length; index++)
            {
                indices[index] = reader.ReadUInt32();
                if (indices[index] >= vertices.Length)
                {
                    throw new InvalidDataException("Mesh artifact contains an out-of-range index.");
                }
            }

            for (int index = 0; index < subMeshes.Length; index++)
            {
                subMeshes[index] = new MeshSubMesh(reader.ReadInt32(), reader.ReadInt32());
                if (subMeshes[index].firstIndex + subMeshes[index].indexCount > indices.Length)
                {
                    throw new InvalidDataException("Mesh artifact contains an out-of-range submesh.");
                }
            }
        }
        catch (EndOfStreamException exception)
        {
            throw new InvalidDataException("Mesh artifact payload is truncated.", exception);
        }

        if (stream.Position != stream.Length)
        {
            throw new InvalidDataException("Mesh artifact payload contains trailing bytes.");
        }

        return new MeshData(vertices, indices, subMeshes);
    }

    private static int RequireCount(int value, string kind)
        => value >= 0 && value <= 100_000_000
            ? value
            : throw new InvalidDataException($"Mesh artifact {kind} count is invalid.");
}
