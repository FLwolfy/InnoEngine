using System;
using System.Collections.Generic;
using System.IO;
using Inno.Core.Mathematics;

namespace Inno.Rendering;

/// <summary>
/// Stores one normalized vertex shared by all rendering backends.
/// </summary>
public readonly record struct GeometryVertex
{
    /// <summary>
    /// Creates a normalized mesh vertex.
    /// </summary>
    /// <param name="position">
    /// Object-space position.
    /// </param>
    /// <param name="normal">
    /// Object-space unit normal.
    /// </param>
    /// <param name="tangent">
    /// Object-space tangent and handedness.
    /// </param>
    /// <param name="textureCoordinate">
    /// Primary texture coordinate.
    /// </param>
    public GeometryVertex(Vector3 position, Vector3 normal, Vector4 tangent, Vector2 textureCoordinate)
    {
        this.position = position;
        this.normal = normal;
        this.tangent = tangent;
        this.textureCoordinate = textureCoordinate;
    }

    /// <summary>
    /// Gets the object-space position.
    /// </summary>
    public Vector3 position { get; }

    /// <summary>
    /// Gets the object-space unit normal.
    /// </summary>
    public Vector3 normal { get; }

    /// <summary>
    /// Gets the object-space tangent and handedness.
    /// </summary>
    public Vector4 tangent { get; }

    /// <summary>
    /// Gets the primary texture coordinate.
    /// </summary>
    public Vector2 textureCoordinate { get; }
}

/// <summary>
/// Identifies a contiguous triangle-index range.
/// </summary>
public readonly record struct GeometrySection
{
    /// <summary>
    /// Creates a submesh range.
    /// </summary>
    /// <param name="firstIndex">
    /// First index in the shared index buffer.
    /// </param>
    /// <param name="indexCount">
    /// Number of indices in the range.
    /// </param>
    public GeometrySection(int firstIndex, int indexCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(firstIndex);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(indexCount);
        this.firstIndex = firstIndex;
        this.indexCount = indexCount;
    }

    /// <summary>
    /// Gets the first index in the shared index buffer.
    /// </summary>
    public int firstIndex { get; }

    /// <summary>
    /// Gets the number of indices in the range.
    /// </summary>
    public int indexCount { get; }
}

/// <summary>
/// Contains normalized CPU mesh data ready for backend upload.
/// </summary>
public sealed class GeometryData
{
    /// <summary>
    /// Creates normalized mesh data.
    /// </summary>
    /// <param name="vertices">
    /// Vertex stream.
    /// </param>
    /// <param name="indices">
    /// Triangle index stream.
    /// </param>
    /// <param name="sections">
    /// Contiguous submesh ranges.
    /// </param>
    public GeometryData(GeometryVertex[] vertices, uint[] indices, GeometrySection[] sections)
    {
        ArgumentNullException.ThrowIfNull(vertices);
        ArgumentNullException.ThrowIfNull(indices);
        ArgumentNullException.ThrowIfNull(sections);
        this.vertices = (GeometryVertex[])vertices.Clone();
        this.indices = (uint[])indices.Clone();
        this.sections = (GeometrySection[])sections.Clone();
    }

    /// <summary>
    /// Gets the normalized vertex stream.
    /// </summary>
    public IReadOnlyList<GeometryVertex> vertices { get; }

    /// <summary>
    /// Gets the triangle index stream.
    /// </summary>
    public IReadOnlyList<uint> indices { get; }

    /// <summary>
    /// Gets contiguous submesh ranges.
    /// </summary>
    public IReadOnlyList<GeometrySection> sections { get; }
}

/// <summary>
/// Decodes normalized CPU geometry committed by mesh importers.
/// </summary>
public static class GeometryAssetRuntime
{
    /// <summary>
    /// Decodes the current committed mesh payload.
    /// </summary>
    /// <param name="geometry">
    /// Imported geometry asset.
    /// </param>
    /// <returns>
    /// Normalized vertices, triangle indices and submeshes.
    /// </returns>
    /// <exception cref="InvalidDataException">
    /// Thrown when the payload is missing or corrupt.
    /// </exception>
    public static GeometryData GetGeometryData(GeometryAsset geometry)
    {
        ArgumentNullException.ThrowIfNull(geometry);
        return GeometryArtifact.Decode(geometry.runtimePayload.Span);
    }
}

/// <summary>
/// Encodes and validates the stable backend-neutral geometry artifact shared by import and runtime stages.
/// </summary>
public static class GeometryArtifact
{
    private const ulong C_MAGIC = 0x4853454D4F4E4E49;

    /// <summary>
    /// Encodes normalized geometry into the immutable runtime artifact layout.
    /// </summary>
    /// <param name="data">
    /// The normalized vertices, indices, and sections to encode.
    /// </param>
    /// <returns>
    /// A complete geometry artifact owned by the caller.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="data"/> is null.
    /// </exception>
    public static byte[] Encode(GeometryData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(C_MAGIC);
        writer.Write(data.vertices.Count);
        writer.Write(data.indices.Count);
        writer.Write(data.sections.Count);
        foreach (GeometryVertex vertex in data.vertices)
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

        foreach (GeometrySection subMesh in data.sections)
        {
            writer.Write(subMesh.firstIndex);
            writer.Write(subMesh.indexCount);
        }

        return stream.ToArray();
    }

    /// <summary>
    /// Decodes and strictly validates one immutable geometry artifact.
    /// </summary>
    /// <param name="bytes">
    /// The complete artifact payload.
    /// </param>
    /// <returns>
    /// The normalized geometry represented by the payload.
    /// </returns>
    /// <exception cref="InvalidDataException">
    /// Thrown when the artifact is empty, corrupt, truncated, or contains invalid ranges.
    /// </exception>
    public static GeometryData Decode(ReadOnlySpan<byte> bytes)
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
        int sectionCount = RequireCount(reader.ReadInt32(), "submesh");
        var vertices = new GeometryVertex[vertexCount];
        var indices = new uint[indexCount];
        var sections = new GeometrySection[sectionCount];
        try
        {
            for (int index = 0; index < vertices.Length; index++)
            {
                vertices[index] = new GeometryVertex(
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

            for (int index = 0; index < sections.Length; index++)
            {
                sections[index] = new GeometrySection(reader.ReadInt32(), reader.ReadInt32());
                if (sections[index].firstIndex + sections[index].indexCount > indices.Length)
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

        return new GeometryData(vertices, indices, sections);
    }

    private static int RequireCount(int value, string kind)
        => value >= 0 && value <= 100_000_000
            ? value
            : throw new InvalidDataException($"Mesh artifact {kind} count is invalid.");
}
