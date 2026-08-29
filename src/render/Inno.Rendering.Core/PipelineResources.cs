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
    /// <summary>Object-space bitangent.</summary>
    Bitangent,
    /// <summary>Primary vertex color.</summary>
    Color0,
    /// <summary>Secondary vertex color.</summary>
    Color1,
    /// <summary>Third vertex color channel.</summary>
    Color2,
    /// <summary>Fourth vertex color channel.</summary>
    Color3,
    /// <summary>Primary texture coordinate.</summary>
    TextureCoordinate0,
    /// <summary>Secondary texture coordinate.</summary>
    TextureCoordinate1,
    /// <summary>Third texture coordinate.</summary>
    TextureCoordinate2,
    /// <summary>Fourth texture coordinate.</summary>
    TextureCoordinate3,
    /// <summary>Fifth texture coordinate.</summary>
    TextureCoordinate4,
    /// <summary>Sixth texture coordinate.</summary>
    TextureCoordinate5,
    /// <summary>Seventh texture coordinate.</summary>
    TextureCoordinate6,
    /// <summary>Eighth texture coordinate.</summary>
    TextureCoordinate7,
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
    /// <summary>One 32-bit floating-point component.</summary>
    Float1,
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
    /// <summary>Two normalized unsigned bytes.</summary>
    UInt8Normalized2,
    /// <summary>Four unsigned bytes interpreted as integers.</summary>
    UInt8Integer4,
    /// <summary>Two unsigned bytes interpreted as integers.</summary>
    UInt8Integer2,
    /// <summary>Four normalized unsigned components packed into 10:10:10:2 bits.</summary>
    UInt10Normalized4,
    /// <summary>Two normalized signed 16-bit components.</summary>
    Int16Normalized2,
    /// <summary>Four normalized signed 16-bit components.</summary>
    Int16Normalized4,
    /// <summary>Two signed 16-bit components interpreted as integers.</summary>
    Int16Integer2,
    /// <summary>Four signed 16-bit components interpreted as integers.</summary>
    Int16Integer4
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
    /// <param name="byteOffset">
    /// Explicit byte offset in the stream, or -1 to place the attribute directly after the preceding attribute.
    /// </param>
    public RenderVertexAttribute(
        RenderVertexSemantic semantic,
        RenderVertexFormat format,
        int byteOffset = -1)
    {
        if (byteOffset < -1)
            throw new ArgumentOutOfRangeException(nameof(byteOffset));
        this.semantic = semantic;
        this.format = format;
        this.byteOffset = byteOffset;
    }

    /// <summary>Gets the shader input semantic.</summary>
    public RenderVertexSemantic semantic { get; }

    /// <summary>Gets the packed component representation.</summary>
    public RenderVertexFormat format { get; }

    /// <summary>
    /// Gets the explicit byte offset in the resolved stream layout, or -1 before a layout resolves automatic placement.
    /// </summary>
    public int byteOffset { get; }

    /// <summary>Gets the packed byte size.</summary>
    public int byteSize => format switch
    {
        RenderVertexFormat.Float1 => 4,
        RenderVertexFormat.Float2 => 8,
        RenderVertexFormat.Float3 => 12,
        RenderVertexFormat.Float4 => 16,
        RenderVertexFormat.Half2 => 4,
        RenderVertexFormat.Half4 => 8,
        RenderVertexFormat.UInt8Normalized2 => 2,
        RenderVertexFormat.UInt8Normalized4 => 4,
        RenderVertexFormat.UInt8Integer2 => 2,
        RenderVertexFormat.UInt8Integer4 => 4,
        RenderVertexFormat.UInt10Normalized4 => 4,
        RenderVertexFormat.Int16Normalized2 => 4,
        RenderVertexFormat.Int16Normalized4 => 8,
        RenderVertexFormat.Int16Integer2 => 4,
        RenderVertexFormat.Int16Integer4 => 8,
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
    /// <param name="attributes">
    /// Unique attributes in ascending byte order. Attributes with offset -1 are packed after the preceding attribute.
    /// </param>
    /// <param name="stride">
    /// Explicit positive stream stride, or zero to use the end of the final attribute. A larger stride preserves
    /// trailing application-defined padding.
    /// </param>
    public RenderVertexLayout(IReadOnlyList<RenderVertexAttribute> attributes, int stride = 0)
    {
        ArgumentNullException.ThrowIfNull(attributes);
        ArgumentOutOfRangeException.ThrowIfNegative(stride);
        if (attributes.Count == 0)
        {
            throw new ArgumentException("A vertex layout requires at least one attribute.", nameof(attributes));
        }

        if (attributes.Select(static value => value.semantic).Distinct().Count() != attributes.Count)
        {
            throw new ArgumentException("A vertex layout cannot repeat a semantic.", nameof(attributes));
        }

        var resolved = new RenderVertexAttribute[attributes.Count];
        int occupiedEnd = 0;
        for (int index = 0; index < attributes.Count; index++)
        {
            RenderVertexAttribute attribute = attributes[index];
            int offset = attribute.byteOffset < 0 ? occupiedEnd : attribute.byteOffset;
            if (offset < occupiedEnd)
            {
                throw new ArgumentException(
                    $"Vertex attribute '{attribute.semantic}' overlaps a preceding attribute.",
                    nameof(attributes));
            }

            resolved[index] = new RenderVertexAttribute(attribute.semantic, attribute.format, offset);
            occupiedEnd = checked(offset + attribute.byteSize);
        }

        if (stride != 0 && stride < occupiedEnd)
        {
            throw new ArgumentException(
                "Vertex stride cannot end before the final attribute.",
                nameof(stride));
        }

        m_attributes = resolved;
        this.stride = stride == 0 ? occupiedEnd : stride;
    }

    /// <summary>Gets attributes in byte-stream order.</summary>
    public IReadOnlyList<RenderVertexAttribute> attributes => m_attributes;

    /// <summary>Gets the interleaved vertex stride in bytes.</summary>
    public int stride { get; }

    /// <inheritdoc />
    public bool Equals(RenderVertexLayout? other)
        => other is not null
            && stride == other.stride
            && m_attributes.SequenceEqual(other.m_attributes);

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

        hash.Add(stride);

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

/// <summary>Selects the primitive assembly used by raster draw commands.</summary>
public enum RenderPrimitiveTopology
{
    /// <summary>Independent triangle triplets.</summary>
    TriangleList,
    /// <summary>Connected triangle strip.</summary>
    TriangleStrip,
    /// <summary>Independent line pairs.</summary>
    LineList,
    /// <summary>Connected line strip.</summary>
    LineStrip,
    /// <summary>Independent points.</summary>
    PointList
}

/// <summary>Selects texture filtering independently from a graphics backend.</summary>
public enum RenderSamplerFilter
{
    /// <summary>Uses nearest-neighbor filtering.</summary>
    Point,
    /// <summary>Uses linear filtering.</summary>
    Linear,
    /// <summary>Uses anisotropic filtering when supported.</summary>
    Anisotropic
}

/// <summary>Selects texture addressing independently for each coordinate axis.</summary>
public enum RenderSamplerAddressMode
{
    /// <summary>Repeats texture coordinates.</summary>
    Repeat,
    /// <summary>Mirrors repeated texture coordinates.</summary>
    Mirror,
    /// <summary>Clamps coordinates to the texture edge.</summary>
    Clamp,
    /// <summary>Samples the backend border color outside the texture.</summary>
    Border
}

/// <summary>Describes one native-serializable backend-neutral sampler binding.</summary>
public struct RenderSamplerState : IEquatable<RenderSamplerState>
{
    /// <summary>Gets linear filtering with clamped addressing.</summary>
    public static RenderSamplerState linearClamp => new(
        RenderSamplerFilter.Linear,
        RenderSamplerAddressMode.Clamp,
        RenderSamplerAddressMode.Clamp,
        RenderSamplerAddressMode.Clamp);

    /// <summary>Creates a sampler state.</summary>
    /// <param name="filter">Minification, magnification, and mip filtering contract.</param>
    /// <param name="addressU">Horizontal address mode.</param>
    /// <param name="addressV">Vertical address mode.</param>
    /// <param name="addressW">Depth or cube address mode.</param>
    public RenderSamplerState(
        RenderSamplerFilter filter,
        RenderSamplerAddressMode addressU,
        RenderSamplerAddressMode addressV,
        RenderSamplerAddressMode addressW)
    {
        this.filter = filter;
        this.addressU = addressU;
        this.addressV = addressV;
        this.addressW = addressW;
    }

    /// <summary>Gets the filter contract.</summary>
    public RenderSamplerFilter filter { get; set; }

    /// <summary>Gets horizontal addressing.</summary>
    public RenderSamplerAddressMode addressU { get; set; }

    /// <summary>Gets vertical addressing.</summary>
    public RenderSamplerAddressMode addressV { get; set; }

    /// <summary>Gets depth or cube addressing.</summary>
    public RenderSamplerAddressMode addressW { get; set; }

    /// <inheritdoc />
    public readonly bool Equals(RenderSamplerState other)
        => filter == other.filter
           && addressU == other.addressU
           && addressV == other.addressV
           && addressW == other.addressW;

    /// <inheritdoc />
    public override readonly bool Equals(object? obj)
        => obj is RenderSamplerState other && Equals(other);

    /// <inheritdoc />
    public override readonly int GetHashCode()
        => HashCode.Combine(filter, addressU, addressV, addressW);

    /// <summary>Determines whether two sampler descriptions are equal.</summary>
    /// <param name="left">Left sampler description.</param>
    /// <param name="right">Right sampler description.</param>
    /// <returns>True when every filtering and addressing field is equal.</returns>
    public static bool operator ==(RenderSamplerState left, RenderSamplerState right)
        => left.Equals(right);

    /// <summary>Determines whether two sampler descriptions differ.</summary>
    /// <param name="left">Left sampler description.</param>
    /// <param name="right">Right sampler description.</param>
    /// <returns>True when at least one filtering or addressing field differs.</returns>
    public static bool operator !=(RenderSamplerState left, RenderSamplerState right)
        => !left.Equals(right);
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
    /// <summary>Shader-readable or writable storage texture.</summary>
    StorageTexture,
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
/// Selects unordered storage-resource access for one shader binding.
/// </summary>
public enum RenderStorageAccess
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
    /// <param name="storageAccess">Storage texture or buffer access.</param>
    public RenderShaderBindingDescriptor(
        RenderBindingId id,
        RenderShaderBindingKind kind,
        int slot = 0,
        RenderUniformType uniformType = RenderUniformType.Vector4,
        int count = 1,
        RenderStorageAccess storageAccess = RenderStorageAccess.Read)
    {
        if (!id.isValid)
        {
            throw new ArgumentException("A shader binding requires a stable manifest name.", nameof(id));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(slot);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);
        if (!Enum.IsDefined(kind))
            throw new ArgumentOutOfRangeException(nameof(kind));
        if (!Enum.IsDefined(storageAccess))
            throw new ArgumentOutOfRangeException(nameof(storageAccess));
        this.id = id;
        this.kind = kind;
        this.slot = slot;
        this.uniformType = uniformType;
        this.count = count;
        this.storageAccess = storageAccess;
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

    /// <summary>Gets storage texture or buffer access.</summary>
    public RenderStorageAccess storageAccess { get; }
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
/// Selects one source or destination blend multiplier.
/// </summary>
public enum RenderBlendFactor
{
    /// <summary>Multiplies by zero.</summary>
    Zero,
    /// <summary>Multiplies by one.</summary>
    One,
    /// <summary>Multiplies by source color.</summary>
    SourceColor,
    /// <summary>Multiplies by one minus source color.</summary>
    InverseSourceColor,
    /// <summary>Multiplies by source alpha.</summary>
    SourceAlpha,
    /// <summary>Multiplies by one minus source alpha.</summary>
    InverseSourceAlpha,
    /// <summary>Multiplies by destination alpha.</summary>
    DestinationAlpha,
    /// <summary>Multiplies by one minus destination alpha.</summary>
    InverseDestinationAlpha,
    /// <summary>Multiplies by destination color.</summary>
    DestinationColor,
    /// <summary>Multiplies by one minus destination color.</summary>
    InverseDestinationColor,
    /// <summary>Uses the saturated source-alpha factor.</summary>
    SourceAlphaSaturate,
    /// <summary>Multiplies by the packed constant blend color.</summary>
    Constant,
    /// <summary>Multiplies by one minus the packed constant blend color.</summary>
    InverseConstant
}

/// <summary>Selects the arithmetic operation combining source and destination blend terms.</summary>
public enum RenderBlendEquation
{
    /// <summary>Adds source and destination terms.</summary>
    Add,
    /// <summary>Subtracts the destination term from the source term.</summary>
    Subtract,
    /// <summary>Subtracts the source term from the destination term.</summary>
    ReverseSubtract,
    /// <summary>Selects the component-wise minimum.</summary>
    Minimum,
    /// <summary>Selects the component-wise maximum.</summary>
    Maximum
}

/// <summary>Describes independent color and alpha blending without backend-native flags.</summary>
public struct RenderBlendState
{
    /// <summary>Gets the disabled opaque blend state.</summary>
    public static RenderBlendState opaque => new()
    {
        enabled = false,
        colorSource = RenderBlendFactor.One,
        colorDestination = RenderBlendFactor.Zero,
        alphaSource = RenderBlendFactor.One,
        alphaDestination = RenderBlendFactor.Zero
    };

    /// <summary>Gets conventional straight-alpha blending.</summary>
    public static RenderBlendState alpha => new()
    {
        enabled = true,
        colorSource = RenderBlendFactor.SourceAlpha,
        colorDestination = RenderBlendFactor.InverseSourceAlpha,
        alphaSource = RenderBlendFactor.One,
        alphaDestination = RenderBlendFactor.InverseSourceAlpha
    };

    /// <summary>Gets additive source-alpha blending.</summary>
    public static RenderBlendState additive => new()
    {
        enabled = true,
        colorSource = RenderBlendFactor.SourceAlpha,
        colorDestination = RenderBlendFactor.One,
        alphaSource = RenderBlendFactor.One,
        alphaDestination = RenderBlendFactor.One
    };

    /// <summary>Gets conventional premultiplied-alpha blending.</summary>
    public static RenderBlendState premultiplied => new()
    {
        enabled = true,
        colorSource = RenderBlendFactor.One,
        colorDestination = RenderBlendFactor.InverseSourceAlpha,
        alphaSource = RenderBlendFactor.One,
        alphaDestination = RenderBlendFactor.InverseSourceAlpha
    };

    /// <summary>Gets or sets whether blending is enabled.</summary>
    public bool enabled { get; set; }

    /// <summary>Gets or sets the source multiplier for RGB channels.</summary>
    public RenderBlendFactor colorSource { get; set; }

    /// <summary>Gets or sets the destination multiplier for RGB channels.</summary>
    public RenderBlendFactor colorDestination { get; set; }

    /// <summary>Gets or sets the RGB combination equation.</summary>
    public RenderBlendEquation colorEquation { get; set; }

    /// <summary>Gets or sets the source multiplier for alpha.</summary>
    public RenderBlendFactor alphaSource { get; set; }

    /// <summary>Gets or sets the destination multiplier for alpha.</summary>
    public RenderBlendFactor alphaDestination { get; set; }

    /// <summary>Gets or sets the alpha combination equation.</summary>
    public RenderBlendEquation alphaEquation { get; set; }

    /// <summary>Gets or sets the packed RGBA8 constant used by constant blend factors.</summary>
    public uint constantRgba { get; set; }

    /// <summary>Gets or sets whether alpha-to-coverage is enabled.</summary>
    public bool alphaToCoverage { get; set; }
}

/// <summary>Selects the comparison applied to stencil reference and stored values.</summary>
public enum RenderStencilCompare
{
    /// <summary>Never passes.</summary>
    Never,
    /// <summary>Passes when reference is smaller.</summary>
    Less,
    /// <summary>Passes when values are equal.</summary>
    Equal,
    /// <summary>Passes when reference is smaller or equal.</summary>
    LessEqual,
    /// <summary>Passes when reference is greater.</summary>
    Greater,
    /// <summary>Passes when values differ.</summary>
    NotEqual,
    /// <summary>Passes when reference is greater or equal.</summary>
    GreaterEqual,
    /// <summary>Always passes.</summary>
    Always
}

/// <summary>Selects the update applied to a stencil value.</summary>
public enum RenderStencilOperation
{
    /// <summary>Keeps the stored value.</summary>
    Keep,
    /// <summary>Clears the stored value to zero.</summary>
    Zero,
    /// <summary>Replaces the stored value with the reference.</summary>
    Replace,
    /// <summary>Increments and clamps the stored value.</summary>
    IncrementClamp,
    /// <summary>Increments and wraps the stored value.</summary>
    IncrementWrap,
    /// <summary>Decrements and clamps the stored value.</summary>
    DecrementClamp,
    /// <summary>Decrements and wraps the stored value.</summary>
    DecrementWrap,
    /// <summary>Bitwise-inverts the stored value.</summary>
    Invert
}

/// <summary>Describes stencil behavior for one triangle face orientation.</summary>
public readonly record struct RenderStencilFaceState
{
    /// <summary>Creates one face stencil state.</summary>
    /// <param name="compare">Stencil comparison.</param>
    /// <param name="fail">Operation after stencil comparison failure.</param>
    /// <param name="depthFail">Operation after stencil success and depth failure.</param>
    /// <param name="pass">Operation after stencil and depth success.</param>
    public RenderStencilFaceState(
        RenderStencilCompare compare,
        RenderStencilOperation fail,
        RenderStencilOperation depthFail,
        RenderStencilOperation pass)
    {
        this.compare = compare;
        this.fail = fail;
        this.depthFail = depthFail;
        this.pass = pass;
    }

    /// <summary>Gets stencil comparison.</summary>
    public RenderStencilCompare compare { get; }

    /// <summary>Gets the stencil-failure operation.</summary>
    public RenderStencilOperation fail { get; }

    /// <summary>Gets the depth-failure operation.</summary>
    public RenderStencilOperation depthFail { get; }

    /// <summary>Gets the complete-pass operation.</summary>
    public RenderStencilOperation pass { get; }
}

/// <summary>Describes complete two-sided stencil state for one draw.</summary>
public sealed class RenderStencilState
{
    /// <summary>Gets disabled stencil state.</summary>
    public static RenderStencilState disabled { get; } = new() { enabled = false };

    /// <summary>Gets whether stencil testing and updates are active.</summary>
    public bool enabled { get; init; }

    /// <summary>Gets the eight-bit stencil reference value.</summary>
    public byte reference { get; init; }

    /// <summary>Gets the mask applied while reading stored stencil.</summary>
    public byte readMask { get; init; } = byte.MaxValue;

    /// <summary>Gets the mask applied while writing stencil.</summary>
    public byte writeMask { get; init; } = byte.MaxValue;

    /// <summary>Gets front-face stencil behavior.</summary>
    public RenderStencilFaceState front { get; init; } = new(
        RenderStencilCompare.Always,
        RenderStencilOperation.Keep,
        RenderStencilOperation.Keep,
        RenderStencilOperation.Keep);

    /// <summary>Gets back-face stencil behavior.</summary>
    public RenderStencilFaceState back { get; init; } = new(
        RenderStencilCompare.Always,
        RenderStencilOperation.Keep,
        RenderStencilOperation.Keep,
        RenderStencilOperation.Keep);
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

    /// <summary>Gets independent RGB and alpha blending.</summary>
    public RenderBlendState blend { get; init; } = RenderBlendState.opaque;

    /// <summary>Gets the four-bit RGBA write mask.</summary>
    public byte colorWriteMask { get; init; } = 0x0f;

    /// <summary>Gets whether multisample rasterization is enabled for compatible targets.</summary>
    public bool multisampling { get; init; } = true;

    /// <summary>Gets primitive assembly for subsequent draw commands.</summary>
    public RenderPrimitiveTopology topology { get; init; } = RenderPrimitiveTopology.TriangleList;
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
