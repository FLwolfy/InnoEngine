using System;
using System.Collections.Generic;
using Inno.Assets.Core;
using Inno.Core.Mathematics;
using Inno.Core.Reflection;
using Inno.Core.Serialization;
using Inno.Rendering.Core;

namespace Inno.Rendering;

/// <summary>
/// Identifies a programmable shader stage.
/// </summary>
[Flags]
public enum ShaderStage
{
    /// <summary>No shader stage.</summary>
    None = 0,
    /// <summary>Vertex shader stage.</summary>
    Vertex = 1 << 0,
    /// <summary>Fragment shader stage.</summary>
    Fragment = 1 << 1,
    /// <summary>Compute shader stage.</summary>
    Compute = 1 << 2
}

/// <summary>
/// Identifies an artist-facing shader property type.
/// </summary>
public enum ShaderPropertyType
{
    /// <summary>Scalar floating-point value.</summary>
    Float,
    /// <summary>Two-component floating-point vector.</summary>
    Vector2,
    /// <summary>Three-component floating-point vector.</summary>
    Vector3,
    /// <summary>Four-component floating-point vector.</summary>
    Vector4,
    /// <summary>Linear RGBA color.</summary>
    Color,
    /// <summary>Four-by-four matrix.</summary>
    Matrix4x4,
    /// <summary>Two-dimensional texture.</summary>
    Texture2D,
    /// <summary>Layered two-dimensional texture.</summary>
    Texture2DArray,
    /// <summary>Cube texture.</summary>
    TextureCube,
    /// <summary>Sampler state.</summary>
    Sampler,
    /// <summary>Structured buffer.</summary>
    Buffer
}

/// <summary>
/// Selects the triangle face rejected by a raster pass.
/// </summary>
public enum ShaderCullMode
{
    /// <summary>Does not reject either face orientation.</summary>
    None,
    /// <summary>Rejects clockwise-facing triangles in pipeline-corrected screen space.</summary>
    Front,
    /// <summary>Rejects counter-clockwise-facing triangles in pipeline-corrected screen space.</summary>
    Back
}

/// <summary>
/// Selects the comparison used by depth testing.
/// </summary>
public enum ShaderCompareFunction
{
    /// <summary>Always rejects the fragment.</summary>
    Never,
    /// <summary>Accepts a fragment with a smaller depth.</summary>
    Less,
    /// <summary>Accepts a fragment with an equal depth.</summary>
    Equal,
    /// <summary>Accepts a fragment with a smaller or equal depth.</summary>
    LessEqual,
    /// <summary>Accepts a fragment with a greater depth.</summary>
    Greater,
    /// <summary>Accepts a fragment with a different depth.</summary>
    NotEqual,
    /// <summary>Accepts a fragment with a greater or equal depth.</summary>
    GreaterEqual,
    /// <summary>Always accepts the fragment.</summary>
    Always
}

/// <summary>
/// Selects a built-in color blending contract.
/// </summary>
public enum ShaderBlendMode
{
    /// <summary>Writes source color without blending.</summary>
    Opaque,
    /// <summary>Uses source alpha for conventional transparency.</summary>
    Alpha,
    /// <summary>Adds source color to the destination.</summary>
    Additive,
    /// <summary>Uses premultiplied source alpha.</summary>
    Premultiplied
}

/// <summary>
/// Declares backend-neutral fixed-function state for one shader pass.
/// </summary>
public sealed class ShaderRenderState
{
    /// <summary>Gets the default opaque raster state.</summary>
    public static ShaderRenderState opaque { get; } = new();

    /// <summary>Gets the face culling mode.</summary>
    public ShaderCullMode cull { get; init; } = ShaderCullMode.Back;

    /// <summary>Gets the depth comparison function.</summary>
    public ShaderCompareFunction depthCompare { get; init; } = ShaderCompareFunction.LessEqual;

    /// <summary>Gets whether accepted fragments update depth.</summary>
    public bool depthWrite { get; init; } = true;

    /// <summary>Gets the color blending mode.</summary>
    public ShaderBlendMode blend { get; init; } = ShaderBlendMode.Opaque;

    /// <summary>Gets the four-bit RGBA color write mask.</summary>
    public byte colorWriteMask { get; init; } = 0x0f;
}

/// <summary>
/// Identifies a material property using a stable serialized string.
/// </summary>
public readonly record struct ShaderPropertyId
{
    /// <summary>
    /// Creates a stable shader property identifier.
    /// </summary>
    /// <param name="value">Stable manifest property identifier.</param>
    public ShaderPropertyId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        this.value = value;
    }

    /// <summary>Gets the stable manifest property identifier.</summary>
    public string value { get; }

    /// <inheritdoc />
    public override string ToString() => value;
}

/// <summary>
/// Declares one shader property and its reflected stage visibility.
/// </summary>
public sealed class ShaderPropertyDefinition
{
    /// <summary>
    /// Creates a shader property definition.
    /// </summary>
    /// <param name="id">Stable property identifier.</param>
    /// <param name="displayName">Artist-facing property name.</param>
    /// <param name="type">Property type.</param>
    /// <param name="stages">Stages that access the property.</param>
    /// <param name="defaultValueJson">Strict neutral JSON default value.</param>
    public ShaderPropertyDefinition(
        ShaderPropertyId id,
        string displayName,
        ShaderPropertyType type,
        ShaderStage stages,
        string defaultValueJson)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentException.ThrowIfNullOrWhiteSpace(defaultValueJson);
        this.id = id;
        this.displayName = displayName;
        this.type = type;
        this.stages = stages;
        this.defaultValueJson = defaultValueJson;
    }

    /// <summary>Gets the stable property identifier.</summary>
    public ShaderPropertyId id { get; }

    /// <summary>Gets the artist-facing property name.</summary>
    public string displayName { get; }

    /// <summary>Gets the property type.</summary>
    public ShaderPropertyType type { get; }

    /// <summary>Gets stages that access the property.</summary>
    public ShaderStage stages { get; }

    /// <summary>Gets the strict neutral JSON default value.</summary>
    public string defaultValueJson { get; }
}

/// <summary>
/// Declares one static shader keyword that may produce compiled variants.
/// </summary>
public sealed class ShaderKeywordDefinition
{
    /// <summary>
    /// Creates a shader keyword definition.
    /// </summary>
    /// <param name="id">Stable keyword identifier.</param>
    /// <param name="options">Allowed stable option identifiers.</param>
    public ShaderKeywordDefinition(string id, IReadOnlyList<string> options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(options);
        this.id = id;
        this.options = options;
    }

    /// <summary>Gets the stable keyword identifier.</summary>
    public string id { get; }

    /// <summary>Gets allowed stable option identifiers.</summary>
    public IReadOnlyList<string> options { get; }
}

/// <summary>
/// Defines one open-tagged raster or compute shader pass.
/// </summary>
public sealed class ShaderPassDefinition
{
    /// <summary>
    /// Creates a shader pass definition.
    /// </summary>
    /// <param name="name">Stable pass name.</param>
    /// <param name="tag">Open pipeline pass tag.</param>
    /// <param name="vertexSource">Optional project-relative vertex source path.</param>
    /// <param name="fragmentSource">Optional project-relative fragment source path.</param>
    /// <param name="computeSource">Optional project-relative compute source path.</param>
    /// <param name="varyingSource">Optional project-relative varying definition path.</param>
    /// <param name="requiredFeatures">Required device capability mask.</param>
    /// <param name="renderState">Backend-neutral fixed-function state.</param>
    /// <param name="tags">Additional open string tags.</param>
    public ShaderPassDefinition(
        string name,
        string tag,
        string? vertexSource,
        string? fragmentSource,
        string? computeSource,
        string? varyingSource,
        GraphicsFeature requiredFeatures = GraphicsFeature.None,
        ShaderRenderState? renderState = null,
        IReadOnlyDictionary<string, string>? tags = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(tag);
        this.name = name;
        this.tag = tag;
        this.vertexSource = vertexSource;
        this.fragmentSource = fragmentSource;
        this.computeSource = computeSource;
        this.varyingSource = varyingSource;
        this.requiredFeatures = requiredFeatures;
        this.renderState = renderState ?? ShaderRenderState.opaque;
        this.tags = tags ?? new Dictionary<string, string>(StringComparer.Ordinal);
    }

    /// <summary>Gets the stable pass name.</summary>
    public string name { get; }

    /// <summary>Gets the open pipeline pass tag.</summary>
    public string tag { get; }

    /// <summary>Gets the vertex source path, if present.</summary>
    public string? vertexSource { get; }

    /// <summary>Gets the fragment source path, if present.</summary>
    public string? fragmentSource { get; }

    /// <summary>Gets the compute source path, if present.</summary>
    public string? computeSource { get; }

    /// <summary>Gets the varying definition path, if present.</summary>
    public string? varyingSource { get; }

    /// <summary>Gets required device capabilities.</summary>
    public GraphicsFeature requiredFeatures { get; }

    /// <summary>Gets backend-neutral fixed-function state.</summary>
    public ShaderRenderState renderState { get; }

    /// <summary>Gets additional open string tags.</summary>
    public IReadOnlyDictionary<string, string> tags { get; }
}

/// <summary>
/// Provides built-in pass-tag protocol values without closing the tag namespace.
/// </summary>
public static class BuiltinShaderPassTags
{
    /// <summary>Forward physically based lighting pass that consumes GPU clustered-light lists.</summary>
    public const string ForwardLitClustered = "ForwardLitClustered";
    /// <summary>Forward physically based lighting pass.</summary>
    public const string ForwardLit = "ForwardLit";
    /// <summary>Deferred geometry-buffer pass.</summary>
    public const string GBuffer = "GBuffer";
    /// <summary>Depth-only pass.</summary>
    public const string DepthOnly = "DepthOnly";
    /// <summary>Shadow-map caster pass.</summary>
    public const string ShadowCaster = "ShadowCaster";
    /// <summary>Editor object identifier pass.</summary>
    public const string Picking = "Picking";
    /// <summary>Fullscreen raster pass.</summary>
    public const string Fullscreen = "Fullscreen";
    /// <summary>Compute kernel pass.</summary>
    public const string Compute = "Compute";
}

/// <summary>
/// Provides stable metadata keys understood by the rendering runtime while keeping values open to projects.
/// </summary>
public static class BuiltinShaderMetadataTags
{
    /// <summary>
    /// Maps a fullscreen or compute pass to the stable operation ID used by a pipeline feature.
    /// </summary>
    public const string PipelineOperation = "PipelineOperation";
}

/// <summary>
/// Contains the backend-neutral source-of-truth definition shared by handwritten and graph shaders.
/// </summary>
public sealed class ShaderDefinition
{
    /// <summary>
    /// Creates a shader definition.
    /// </summary>
    /// <param name="name">Artist-facing shader name.</param>
    /// <param name="properties">Stable property declarations.</param>
    /// <param name="keywords">Static keyword declarations.</param>
    /// <param name="passes">Open-tagged pass declarations.</param>
    public ShaderDefinition(
        string name,
        IReadOnlyList<ShaderPropertyDefinition> properties,
        IReadOnlyList<ShaderKeywordDefinition> keywords,
        IReadOnlyList<ShaderPassDefinition> passes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(properties);
        ArgumentNullException.ThrowIfNull(keywords);
        ArgumentNullException.ThrowIfNull(passes);
        this.name = name;
        this.properties = properties;
        this.keywords = keywords;
        this.passes = passes;
    }

    /// <summary>Gets the artist-facing shader name.</summary>
    public string name { get; }

    /// <summary>Gets stable property declarations.</summary>
    public IReadOnlyList<ShaderPropertyDefinition> properties { get; }

    /// <summary>Gets static keyword declarations.</summary>
    public IReadOnlyList<ShaderKeywordDefinition> keywords { get; }

    /// <summary>Gets open-tagged pass declarations.</summary>
    public IReadOnlyList<ShaderPassDefinition> passes { get; }
}

/// <summary>
/// Represents an imported handwritten or generated shader definition and compiled artifact payload.
/// </summary>
[StableTypeId("e6672287-145f-4f51-8380-a6aeaf57a801")]
public class ShaderAsset : AssetObject
{
    [SerializableProperty(PropertyVisibility.Hide)]
    private string m_definitionJson = string.Empty;

    /// <summary>Gets the currently committed backend-neutral definition.</summary>
    public ShaderDefinition? definition { get; private set; }

    /// <summary>Commits a validated backend-neutral definition from an importer or generated shader asset.</summary>
    /// <param name="value">Complete definition that becomes visible to materials and target compilation.</param>
    protected internal void SetDefinition(ShaderDefinition value)
    {
        ArgumentNullException.ThrowIfNull(value);
        definition = value;
        m_definitionJson = ShaderDefinitionCodec.Encode(value);
    }

    [OnSerializableRestored]
    private void OnSerializableRestored()
    {
        definition = string.IsNullOrWhiteSpace(m_definitionJson)
            ? null
            : ShaderDefinitionCodec.Decode(m_definitionJson);
    }
}

/// <summary>
/// Selects how texture samples are decoded for shader use.
/// </summary>
public enum TextureColorSpace
{
    /// <summary>Samples are interpreted as linear values.</summary>
    Linear,
    /// <summary>Color samples are decoded from sRGB at texture sampling time.</summary>
    Srgb
}

/// <summary>
/// Represents imported texture content; GPU residency belongs to the rendering cache.
/// </summary>
[StableTypeId("e174b6eb-f79a-470f-a460-84f88ab49d0e")]
public sealed class TextureAsset : AssetObject
{
    /// <summary>Gets the source pixel width.</summary>
    [SerializableProperty]
    public int width { get; internal set; }

    /// <summary>Gets the source pixel height.</summary>
    [SerializableProperty]
    public int height { get; internal set; }

    /// <summary>Gets the declared sample color space.</summary>
    [SerializableProperty]
    public TextureColorSpace colorSpace { get; internal set; }

    /// <summary>Gets the normalized source container name.</summary>
    [SerializableProperty]
    public string sourceFormat { get; internal set; } = string.Empty;
}

/// <summary>
/// Represents imported mesh content; GPU residency belongs to the rendering cache.
/// </summary>
[StableTypeId("c5b31b63-d9a8-4b5c-9280-21f0cc8405a8")]
public sealed class MeshAsset : AssetObject
{
    /// <summary>Gets the number of normalized mesh vertices.</summary>
    [SerializableProperty]
    public int vertexCount { get; internal set; }

    /// <summary>Gets the number of triangle indices.</summary>
    [SerializableProperty]
    public int indexCount { get; internal set; }

    /// <summary>Gets the number of independently submitted submeshes.</summary>
    [SerializableProperty]
    public int subMeshCount { get; internal set; }

    /// <summary>Gets the object-space center of the imported geometry bounds.</summary>
    [SerializableProperty]
    public Vector3 boundsCenter { get; internal set; }

    /// <summary>Gets the non-negative object-space half-extents of the imported geometry bounds.</summary>
    [SerializableProperty]
    public Vector3 boundsExtents { get; internal set; }
}
