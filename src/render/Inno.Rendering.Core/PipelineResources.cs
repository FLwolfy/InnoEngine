using System;
using System.Collections.Generic;
using System.Linq;

namespace Inno.Rendering.Core;

/// <summary>
/// Identifies one backend-neutral vertex attribute semantic.
/// </summary>
public enum RenderVertexSemantic
{
    /// <summary>Object-space position.</summary>
    Position,
    /// <summary>Object-space normal.</summary>
    Normal,
    /// <summary>Object-space tangent and handedness.</summary>
    Tangent,
    /// <summary>Primary vertex color.</summary>
    Color0,
    /// <summary>Primary texture coordinate.</summary>
    TextureCoordinate0,
    /// <summary>Secondary texture coordinate.</summary>
    TextureCoordinate1,
    /// <summary>Skinning indices.</summary>
    BlendIndices,
    /// <summary>Skinning weights.</summary>
    BlendWeights
}

/// <summary>
/// Identifies one packed vertex attribute representation.
/// </summary>
public enum RenderVertexFormat
{
    /// <summary>Two 32-bit floating-point components.</summary>
    Float2,
    /// <summary>Three 32-bit floating-point components.</summary>
    Float3,
    /// <summary>Four 32-bit floating-point components.</summary>
    Float4,
    /// <summary>Two 16-bit floating-point components.</summary>
    Half2,
    /// <summary>Four 16-bit floating-point components.</summary>
    Half4,
    /// <summary>Four normalized unsigned bytes.</summary>
    UInt8Normalized4,
    /// <summary>Four unsigned bytes interpreted as integers.</summary>
    UInt8Integer4,
    /// <summary>Four normalized signed 16-bit components.</summary>
    Int16Normalized4
}

/// <summary>
/// Describes one vertex attribute in stream order.
/// </summary>
public readonly record struct RenderVertexAttribute
{
    /// <summary>
    /// Creates a vertex attribute declaration.
    /// </summary>
    /// <param name="semantic">Shader input semantic.</param>
    /// <param name="format">Packed component representation.</param>
    public RenderVertexAttribute(RenderVertexSemantic semantic, RenderVertexFormat format)
    {
        this.semantic = semantic;
        this.format = format;
    }

    /// <summary>Gets the shader input semantic.</summary>
    public RenderVertexSemantic semantic { get; }

    /// <summary>Gets the packed component representation.</summary>
    public RenderVertexFormat format { get; }

    /// <summary>Gets the packed byte size.</summary>
    public int byteSize => format switch
    {
        RenderVertexFormat.Float2 => 8,
        RenderVertexFormat.Float3 => 12,
        RenderVertexFormat.Float4 => 16,
        RenderVertexFormat.Half2 => 4,
        RenderVertexFormat.Half4 => 8,
        RenderVertexFormat.UInt8Normalized4 => 4,
        RenderVertexFormat.UInt8Integer4 => 4,
        RenderVertexFormat.Int16Normalized4 => 8,
        _ => throw new ArgumentOutOfRangeException(nameof(format))
    };
}

/// <summary>
/// Defines one interleaved vertex stream independently from a graphics backend.
/// </summary>
public sealed class RenderVertexLayout : IEquatable<RenderVertexLayout>
{
    private readonly IReadOnlyList<RenderVertexAttribute> m_attributes;

    /// <summary>
    /// Creates an interleaved vertex layout.
    /// </summary>
    /// <param name="attributes">Unique attributes in byte-stream order.</param>
    public RenderVertexLayout(IReadOnlyList<RenderVertexAttribute> attributes)
    {
        ArgumentNullException.ThrowIfNull(attributes);
        if (attributes.Count == 0)
        {
            throw new ArgumentException("A vertex layout requires at least one attribute.", nameof(attributes));
        }

        if (attributes.Select(static value => value.semantic).Distinct().Count() != attributes.Count)
        {
            throw new ArgumentException("A vertex layout cannot repeat a semantic.", nameof(attributes));
        }

        m_attributes = attributes.ToArray();
        stride = m_attributes.Sum(static value => value.byteSize);
    }

    /// <summary>Gets attributes in byte-stream order.</summary>
    public IReadOnlyList<RenderVertexAttribute> attributes => m_attributes;

    /// <summary>Gets the interleaved vertex stride in bytes.</summary>
    public int stride { get; }

    /// <inheritdoc />
    public bool Equals(RenderVertexLayout? other)
        => other is not null && m_attributes.SequenceEqual(other.m_attributes);

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as RenderVertexLayout);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        HashCode hash = new();
        foreach (RenderVertexAttribute attribute in m_attributes)
        {
            hash.Add(attribute);
        }

        return hash.ToHashCode();
    }
}

/// <summary>
/// Selects the integer representation of an index buffer.
/// </summary>
public enum RenderIndexFormat
{
    /// <summary>Unsigned 16-bit indices.</summary>
    UInt16,
    /// <summary>Unsigned 32-bit indices.</summary>
    UInt32
}

/// <summary>
/// Describes a persistent buffer and any vertex/index interpretation required at creation.
/// </summary>
public sealed class PersistentBufferDescriptor
{
    /// <summary>
    /// Creates a persistent buffer descriptor.
    /// </summary>
    /// <param name="buffer">Capacity and usage.</param>
    /// <param name="vertexLayout">Required interleaved layout for vertex buffers.</param>
    /// <param name="indexFormat">Index representation for index buffers.</param>
    public PersistentBufferDescriptor(
        RenderBufferDescriptor buffer,
        RenderVertexLayout? vertexLayout = null,
        RenderIndexFormat indexFormat = RenderIndexFormat.UInt32)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        if (buffer.usage == 0)
        {
            throw new ArgumentException("A persistent buffer requires at least one usage.", nameof(buffer));
        }

        if ((buffer.usage & (RenderBufferUsage.Vertex | RenderBufferUsage.Index))
            == (RenderBufferUsage.Vertex | RenderBufferUsage.Index))
        {
            throw new ArgumentException("A buffer cannot be both vertex and index input.", nameof(buffer));
        }

        if ((buffer.usage & RenderBufferUsage.Vertex) != 0 && vertexLayout is null)
        {
            throw new ArgumentException("A vertex buffer requires an interleaved layout.", nameof(vertexLayout));
        }

        if (vertexLayout is not null && vertexLayout.stride != buffer.elementStride)
        {
            throw new ArgumentException("Vertex layout stride must match buffer element stride.", nameof(vertexLayout));
        }

        int expectedIndexStride = indexFormat == RenderIndexFormat.UInt16 ? 2 : 4;
        if ((buffer.usage & RenderBufferUsage.Index) != 0
            && buffer.elementStride != expectedIndexStride)
        {
            throw new ArgumentException("Index format must match buffer element stride.", nameof(indexFormat));
        }

        this.buffer = buffer;
        this.vertexLayout = vertexLayout;
        this.indexFormat = indexFormat;
    }

    /// <summary>Gets buffer capacity and usage.</summary>
    public RenderBufferDescriptor buffer { get; }

    /// <summary>Gets the vertex layout when this is a vertex buffer.</summary>
    public RenderVertexLayout? vertexLayout { get; }

    /// <summary>Gets the index representation when this is an index buffer.</summary>
    public RenderIndexFormat indexFormat { get; }
}

/// <summary>
/// Identifies a shader interface binding domain.
/// </summary>
public enum RenderShaderBindingKind
{
    /// <summary>Vector or matrix uniform data.</summary>
    Uniform,
    /// <summary>Sampled texture and sampler state.</summary>
    Texture,
    /// <summary>Compute-readable or writable buffer.</summary>
    StorageBuffer
}

/// <summary>
/// Identifies the native-independent shape of one uniform binding.
/// </summary>
public enum RenderUniformType
{
    /// <summary>Four-component 32-bit floating-point vector.</summary>
    Vector4,
    /// <summary>Three-by-three 32-bit floating-point matrix.</summary>
    Matrix3x3,
    /// <summary>Four-by-four 32-bit floating-point matrix.</summary>
    Matrix4x4
}

/// <summary>
/// Selects compute-buffer access for one shader binding.
/// </summary>
public enum RenderBufferBindingAccess
{
    /// <summary>Shader read-only access.</summary>
    Read,
    /// <summary>Shader write-only access.</summary>
    Write,
    /// <summary>Shader read and write access.</summary>
    ReadWrite
}

/// <summary>
/// Declares one manifest-derived shader binding used for reflection validation.
/// </summary>
public sealed class RenderShaderBindingDescriptor
{
    /// <summary>
    /// Creates a shader binding descriptor.
    /// </summary>
    /// <param name="id">Stable manifest binding name.</param>
    /// <param name="kind">Binding domain.</param>
    /// <param name="slot">Backend-neutral texture or storage slot.</param>
    /// <param name="uniformType">Uniform shape when <paramref name="kind"/> is Uniform.</param>
    /// <param name="count">Uniform array element count.</param>
    /// <param name="bufferAccess">Storage-buffer access.</param>
    public RenderShaderBindingDescriptor(
        RenderBindingId id,
        RenderShaderBindingKind kind,
        int slot = 0,
        RenderUniformType uniformType = RenderUniformType.Vector4,
        int count = 1,
        RenderBufferBindingAccess bufferAccess = RenderBufferBindingAccess.Read)
    {
        if (!id.isValid)
        {
            throw new ArgumentException("A shader binding requires a stable manifest name.", nameof(id));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(slot);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);
        this.id = id;
        this.kind = kind;
        this.slot = slot;
        this.uniformType = uniformType;
        this.count = count;
        this.bufferAccess = bufferAccess;
    }

    /// <summary>Gets the stable manifest binding name.</summary>
    public RenderBindingId id { get; }

    /// <summary>Gets the binding domain.</summary>
    public RenderShaderBindingKind kind { get; }

    /// <summary>Gets the backend-neutral texture or storage slot.</summary>
    public int slot { get; }

    /// <summary>Gets the uniform shape.</summary>
    public RenderUniformType uniformType { get; }

    /// <summary>Gets uniform array element count.</summary>
    public int count { get; }

    /// <summary>Gets storage-buffer access.</summary>
    public RenderBufferBindingAccess bufferAccess { get; }
}

/// <summary>
/// Selects triangle culling for a backend-neutral raster pipeline.
/// </summary>
public enum RenderCullMode
{
    /// <summary>Disables face culling.</summary>
    None,
    /// <summary>Culls front-facing triangles.</summary>
    Front,
    /// <summary>Culls back-facing triangles.</summary>
    Back
}

/// <summary>
/// Selects the winding order interpreted as the front face.
/// </summary>
public enum RenderFrontFace
{
    /// <summary>Clockwise vertices form a front-facing triangle.</summary>
    Clockwise,
    /// <summary>Counter-clockwise vertices form a front-facing triangle.</summary>
    CounterClockwise
}

/// <summary>
/// Selects depth comparison for a backend-neutral raster pipeline.
/// </summary>
public enum RenderDepthCompare
{
    /// <summary>Always rejects.</summary>
    Never,
    /// <summary>Accepts smaller depth.</summary>
    Less,
    /// <summary>Accepts equal depth.</summary>
    Equal,
    /// <summary>Accepts smaller or equal depth.</summary>
    LessEqual,
    /// <summary>Accepts greater depth.</summary>
    Greater,
    /// <summary>Accepts unequal depth.</summary>
    NotEqual,
    /// <summary>Accepts greater or equal depth.</summary>
    GreaterEqual,
    /// <summary>Always accepts.</summary>
    Always
}

/// <summary>
/// Selects a common cross-platform color blend contract.
/// </summary>
public enum RenderBlendMode
{
    /// <summary>Disables blending.</summary>
    Opaque,
    /// <summary>Uses source alpha over destination color.</summary>
    Alpha,
    /// <summary>Adds source color using source alpha.</summary>
    Additive,
    /// <summary>Uses premultiplied source alpha over destination color.</summary>
    Premultiplied
}

/// <summary>
/// Stores backend-neutral fixed-function raster state.
/// </summary>
public sealed class RenderRasterState
{
    /// <summary>Gets the default opaque raster state.</summary>
    public static RenderRasterState opaque { get; } = new();

    /// <summary>Gets the face culling mode.</summary>
    public RenderCullMode cull { get; init; } = RenderCullMode.Back;

    /// <summary>Gets the winding order interpreted as the front face.</summary>
    public RenderFrontFace frontFace { get; init; } = RenderFrontFace.CounterClockwise;

    /// <summary>Gets depth comparison.</summary>
    public RenderDepthCompare depthCompare { get; init; } = RenderDepthCompare.LessEqual;

    /// <summary>Gets whether accepted fragments update depth.</summary>
    public bool depthWrite { get; init; } = true;

    /// <summary>Gets color blending.</summary>
    public RenderBlendMode blend { get; init; } = RenderBlendMode.Opaque;

    /// <summary>Gets the four-bit RGBA write mask.</summary>
    public byte colorWriteMask { get; init; } = 0x0f;

    /// <summary>Gets whether multisample rasterization is enabled for compatible targets.</summary>
    public bool multisampling { get; init; } = true;
}

/// <summary>
/// Describes a graphics program candidate and reflected interface contract.
/// </summary>
public sealed class GraphicsPipelineDescriptor
{
    private readonly byte[] m_vertexShader;
    private readonly byte[] m_fragmentShader;
    private readonly IReadOnlyList<RenderShaderBindingDescriptor> m_bindings;

    /// <summary>
    /// Creates a graphics pipeline descriptor.
    /// </summary>
    /// <param name="vertexShader">Target backend vertex shader binary.</param>
    /// <param name="fragmentShader">Target backend fragment shader binary.</param>
    /// <param name="bindings">Manifest-derived interface contract.</param>
    /// <param name="vertexLayout">Required mesh vertex layout, or <see langword="null"/> for procedural vertices.</param>
    /// <param name="rasterState">Fixed-function raster state.</param>
    public GraphicsPipelineDescriptor(
        ReadOnlySpan<byte> vertexShader,
        ReadOnlySpan<byte> fragmentShader,
        IReadOnlyList<RenderShaderBindingDescriptor> bindings,
        RenderVertexLayout? vertexLayout,
        RenderRasterState? rasterState = null)
    {
        if (vertexShader.IsEmpty)
        {
            throw new ArgumentException("Vertex shader binary cannot be empty.", nameof(vertexShader));
        }

        if (fragmentShader.IsEmpty)
        {
            throw new ArgumentException("Fragment shader binary cannot be empty.", nameof(fragmentShader));
        }

        ArgumentNullException.ThrowIfNull(bindings);
        EnsureUniqueBindings(bindings);
        m_vertexShader = vertexShader.ToArray();
        m_fragmentShader = fragmentShader.ToArray();
        m_bindings = bindings.ToArray();
        this.vertexLayout = vertexLayout;
        this.rasterState = rasterState ?? RenderRasterState.opaque;
    }

    /// <summary>Gets the target backend vertex shader binary.</summary>
    public ReadOnlyMemory<byte> vertexShader => m_vertexShader;

    /// <summary>Gets the target backend fragment shader binary.</summary>
    public ReadOnlyMemory<byte> fragmentShader => m_fragmentShader;

    /// <summary>Gets the manifest-derived interface contract.</summary>
    public IReadOnlyList<RenderShaderBindingDescriptor> bindings => m_bindings;

    /// <summary>Gets the required mesh vertex layout, or <see langword="null"/> for procedural vertices.</summary>
    public RenderVertexLayout? vertexLayout { get; }

    /// <summary>Gets fixed-function raster state.</summary>
    public RenderRasterState rasterState { get; }

    private static void EnsureUniqueBindings(IReadOnlyList<RenderShaderBindingDescriptor> bindings)
    {
        if (bindings.Select(static value => value.id).Distinct().Count() != bindings.Count)
        {
            throw new ArgumentException("Pipeline bindings require unique stable IDs.", nameof(bindings));
        }
    }
}

/// <summary>
/// Describes a compute program candidate and reflected interface contract.
/// </summary>
public sealed class ComputePipelineDescriptor
{
    private readonly byte[] m_computeShader;
    private readonly IReadOnlyList<RenderShaderBindingDescriptor> m_bindings;

    /// <summary>
    /// Creates a compute pipeline descriptor.
    /// </summary>
    /// <param name="computeShader">Target backend compute shader binary.</param>
    /// <param name="bindings">Manifest-derived interface contract.</param>
    public ComputePipelineDescriptor(
        ReadOnlySpan<byte> computeShader,
        IReadOnlyList<RenderShaderBindingDescriptor> bindings)
    {
        if (computeShader.IsEmpty)
        {
            throw new ArgumentException("Compute shader binary cannot be empty.", nameof(computeShader));
        }

        ArgumentNullException.ThrowIfNull(bindings);
        if (bindings.Select(static value => value.id).Distinct().Count() != bindings.Count)
        {
            throw new ArgumentException("Pipeline bindings require unique stable IDs.", nameof(bindings));
        }

        m_computeShader = computeShader.ToArray();
        m_bindings = bindings.ToArray();
    }

    /// <summary>Gets the target backend compute shader binary.</summary>
    public ReadOnlyMemory<byte> computeShader => m_computeShader;

    /// <summary>Gets the manifest-derived interface contract.</summary>
    public IReadOnlyList<RenderShaderBindingDescriptor> bindings => m_bindings;
}
