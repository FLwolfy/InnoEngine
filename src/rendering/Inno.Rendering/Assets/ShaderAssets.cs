using System;
using System.Collections.Generic;
using System.Linq;
using Inno.Assets;
using Inno.Core.Mathematics;
using Inno.Extensibility.Types;
using Inno.Core.Serialization;
using Inno.Rendering;
using Inno.Scripting.Api;

namespace Inno.Rendering;

/// <summary>
/// Identifies a programmable shader stage.
/// </summary>
[Flags]
public enum ShaderStage
{
    /// <summary>
    /// No shader stage.
    /// </summary>
    None = 0,
    /// <summary>
    /// Vertex shader stage.
    /// </summary>
    Vertex = 1 << 0,
    /// <summary>
    /// Fragment shader stage.
    /// </summary>
    Fragment = 1 << 1,
    /// <summary>
    /// Compute shader stage.
    /// </summary>
    Compute = 1 << 2
}

/// <summary>
/// Identifies an artist-facing shader property type.
/// </summary>
public enum ShaderPropertyType
{
    /// <summary>
    /// Scalar floating-point value.
    /// </summary>
    Float,
    /// <summary>
    /// Two-component floating-point vector.
    /// </summary>
    Vector2,
    /// <summary>
    /// Three-component floating-point vector.
    /// </summary>
    Vector3,
    /// <summary>
    /// Four-component floating-point vector.
    /// </summary>
    Vector4,
    /// <summary>
    /// Linear RGBA color.
    /// </summary>
    Color,
    /// <summary>
    /// Four-by-four matrix.
    /// </summary>
    Matrix4x4,
    /// <summary>
    /// Two-dimensional texture.
    /// </summary>
    Texture2D,
    /// <summary>
    /// Layered two-dimensional texture.
    /// </summary>
    Texture2DArray,
    /// <summary>
    /// Three-dimensional volume texture.
    /// </summary>
    Texture3D,
    /// <summary>
    /// Cube texture.
    /// </summary>
    TextureCube,
    /// <summary>
    /// Read-only or read-write buffer.
    /// </summary>
    Buffer
}

/// <summary>
/// Defines how one shader property enters the backend-neutral resource interface.
/// </summary>
public enum ShaderPropertyBindingKind
{
    /// <summary>
    /// Vector or matrix uniform data.
    /// </summary>
    Uniform,
    /// <summary>
    /// Texture sampled through an explicit material sampler.
    /// </summary>
    SampledTexture,
    /// <summary>
    /// Texture bound for unordered shader access by a Pipeline.
    /// </summary>
    StorageTexture,
    /// <summary>
    /// Buffer bound for unordered shader access by a Pipeline.
    /// </summary>
    StorageBuffer
}

/// <summary>
/// Selects the programmable stage combination of a pass.
/// </summary>
public enum ShaderProgramKind
{
    /// <summary>
    /// Vertex and fragment stages used by a raster pass.
    /// </summary>
    Raster,
    /// <summary>
    /// A compute stage used by a compute pass.
    /// </summary>
    Compute
}

/// <summary>
/// Selects the triangle face rejected by a raster pass.
/// </summary>
public enum ShaderCullMode
{
    /// <summary>
    /// Does not reject either face orientation.
    /// </summary>
    None,
    /// <summary>
    /// Rejects front-facing triangles.
    /// </summary>
    Front,
    /// <summary>
    /// Rejects back-facing triangles.
    /// </summary>
    Back
}

/// <summary>
/// Selects the comparison used by depth testing.
/// </summary>
public enum ShaderCompareFunction
{
    /// <summary>
    /// Always rejects the fragment.
    /// </summary>
    Never,
    /// <summary>
    /// Accepts a fragment with a smaller depth.
    /// </summary>
    Less,
    /// <summary>
    /// Accepts a fragment with an equal depth.
    /// </summary>
    Equal,
    /// <summary>
    /// Accepts a fragment with a smaller or equal depth.
    /// </summary>
    LessEqual,
    /// <summary>
    /// Accepts a fragment with a greater depth.
    /// </summary>
    Greater,
    /// <summary>
    /// Accepts a fragment with a different depth.
    /// </summary>
    NotEqual,
    /// <summary>
    /// Accepts a fragment with a greater or equal depth.
    /// </summary>
    GreaterEqual,
    /// <summary>
    /// Always accepts the fragment.
    /// </summary>
    Always
}

/// <summary>
/// Declares backend-neutral fixed-function state for one shader pass.
/// </summary>
public struct ShaderRenderState
{
    /// <summary>
    /// Gets the default opaque raster state.
    /// </summary>
    public static ShaderRenderState opaque => new()
    {
        topology = RenderPrimitiveTopology.TriangleList,
        cull = ShaderCullMode.Back,
        frontFace = RenderFrontFace.CounterClockwise,
        depthCompare = ShaderCompareFunction.LessEqual,
        depthWrite = true,
        blend = RenderBlendState.opaque,
        colorWriteMask = 0x0f,
        multisampling = true
    };

    /// <summary>
    /// Gets or sets the primitive assembly used by raster draws.
    /// </summary>
    public RenderPrimitiveTopology topology { get; set; }

    /// <summary>
    /// Gets or sets the face culling mode.
    /// </summary>
    public ShaderCullMode cull { get; set; }

    /// <summary>
    /// Gets or sets the winding order interpreted as the front face.
    /// </summary>
    public RenderFrontFace frontFace { get; set; }

    /// <summary>
    /// Gets or sets the depth comparison function.
    /// </summary>
    public ShaderCompareFunction depthCompare { get; set; }

    /// <summary>
    /// Gets or sets whether accepted fragments update depth.
    /// </summary>
    public bool depthWrite { get; set; }

    /// <summary>
    /// Gets or sets independent RGB and alpha blending.
    /// </summary>
    public RenderBlendState blend { get; set; }

    /// <summary>
    /// Gets or sets the four-bit RGBA color write mask.
    /// </summary>
    public byte colorWriteMask { get; set; }

    /// <summary>
    /// Gets or sets whether compatible targets use multisample rasterization.
    /// </summary>
    public bool multisampling { get; set; }
}

/// <summary>
/// Identifies a material property using a stable serialized string.
/// </summary>
public record struct ShaderPropertyId
{
    /// <summary>
    /// Creates a stable shader property identifier.
    /// </summary>
    /// <param name="value">
    /// Stable manifest property identifier.
    /// </param>
    public ShaderPropertyId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        this.value = value;
    }

    /// <summary>
    /// Gets or sets the stable manifest property identifier.
    /// </summary>
    public string value { get; set; }

    /// <summary>
    /// Gets whether this identifier has a usable value.
    /// </summary>
    public readonly bool isValid => !string.IsNullOrWhiteSpace(value);

    /// <summary>
    /// Formats this value as a human-readable representation.
    /// </summary>
    /// <returns>
    /// The human-readable representation of this value.
    /// </returns>
    public readonly override string ToString() => value ?? string.Empty;
}

/// <summary>
/// Identifies an open shader and pipeline compatibility contract.
/// </summary>
public record struct ShaderContractId
{
    /// <summary>
    /// Creates a shader contract identifier.
    /// </summary>
    /// <param name="value">
    /// Globally stable contract value.
    /// </param>
    public ShaderContractId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        this.value = value;
    }

    /// <summary>
    /// Gets or sets the globally stable contract value.
    /// </summary>
    public string value { get; set; }

    /// <summary>
    /// Gets whether the identifier has a usable value.
    /// </summary>
    public readonly bool isValid => !string.IsNullOrWhiteSpace(value);

    /// <summary>
    /// Formats this value as a human-readable representation.
    /// </summary>
    /// <returns>
    /// The human-readable representation of this value.
    /// </returns>
    public readonly override string ToString() => value ?? string.Empty;
}

/// <summary>
/// Identifies one material-selectable technique in a shader.
/// </summary>
public record struct ShaderTechniqueId
{
    /// <summary>
    /// Creates a shader technique identifier.
    /// </summary>
    /// <param name="value">
    /// Stable technique value within a shader.
    /// </param>
    public ShaderTechniqueId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        this.value = value;
    }

    /// <summary>
    /// Gets or sets the stable technique value.
    /// </summary>
    public string value { get; set; }

    /// <summary>
    /// Gets whether the identifier has a usable value.
    /// </summary>
    public readonly bool isValid => !string.IsNullOrWhiteSpace(value);

    /// <summary>
    /// Formats this value as a human-readable representation.
    /// </summary>
    /// <returns>
    /// The human-readable representation of this value.
    /// </returns>
    public readonly override string ToString() => value ?? string.Empty;
}

/// <summary>
/// Identifies an open pass purpose defined by a rendering provider.
/// </summary>
public record struct ShaderPassRoleId
{
    /// <summary>
    /// Creates a shader pass role identifier.
    /// </summary>
    /// <param name="value">
    /// Stable role value within a contract.
    /// </param>
    public ShaderPassRoleId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        this.value = value;
    }

    /// <summary>
    /// Gets or sets the stable role value.
    /// </summary>
    public string value { get; set; }

    /// <summary>
    /// Gets whether the identifier has a usable value.
    /// </summary>
    public readonly bool isValid => !string.IsNullOrWhiteSpace(value);

    /// <summary>
    /// Formats this value as a human-readable representation.
    /// </summary>
    /// <returns>
    /// The human-readable representation of this value.
    /// </returns>
    public readonly override string ToString() => value ?? string.Empty;
}

/// <summary>
/// Stores one open metadata key and value.
/// </summary>
public struct ShaderMetadataEntry
{
    /// <summary>
    /// Creates one metadata entry.
    /// </summary>
    /// <param name="key">
    /// Stable provider-defined key.
    /// </param>
    /// <param name="value">
    /// Provider-defined value.
    /// </param>
    public ShaderMetadataEntry(string key, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        this.key = key;
        this.value = value ?? string.Empty;
    }

    /// <summary>
    /// Gets or sets the stable metadata key.
    /// </summary>
    public string key { get; set; }

    /// <summary>
    /// Gets or sets the metadata value.
    /// </summary>
    public string value { get; set; }
}

/// <summary>
/// Declares one shader property and its reflected stage visibility.
/// </summary>
public struct ShaderPropertyDefinition
{
    /// <summary>
    /// Creates a shader property definition.
    /// </summary>
    /// <param name="id">
    /// Stable property identifier.
    /// </param>
    /// <param name="displayName">
    /// Artist-facing property name.
    /// </param>
    /// <param name="type">
    /// Property type.
    /// </param>
    /// <param name="stages">
    /// Stages that access the property.
    /// </param>
    /// <param name="defaultValue">
    /// Native serializable default value.
    /// </param>
    /// <param name="bindingKind">
    /// Optional explicit binding domain. When omitted, numeric values become uniforms, textures become sampled
    /// textures, and buffers become storage buffers.
    /// </param>
    /// <param name="storageAccess">
    /// Required access for storage texture or buffer bindings.
    /// </param>
    public ShaderPropertyDefinition(
        ShaderPropertyId id,
        string displayName,
        ShaderPropertyType type,
        ShaderStage stages,
        MaterialValue defaultValue,
        ShaderPropertyBindingKind? bindingKind = null,
        RenderStorageAccess storageAccess = RenderStorageAccess.Read)
    {
        if (!id.isValid)
            throw new ArgumentException("A shader property ID must be valid.", nameof(id));
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        this.id = id;
        this.displayName = displayName;
        this.type = type;
        this.stages = stages;
        this.defaultValue = defaultValue;
        this.bindingKind = bindingKind ?? InferBindingKind(type);
        this.storageAccess = storageAccess;
        if (!Enum.IsDefined(storageAccess))
            throw new ArgumentOutOfRangeException(nameof(storageAccess));
        ValidateBindingKind(type, this.bindingKind);
    }

    /// <summary>
    /// Gets or sets the stable property identifier.
    /// </summary>
    public ShaderPropertyId id { get; set; }

    /// <summary>
    /// Gets or sets the artist-facing property name.
    /// </summary>
    public string displayName { get; set; }

    /// <summary>
    /// Gets or sets the property type.
    /// </summary>
    public ShaderPropertyType type { get; set; }

    /// <summary>
    /// Gets or sets stages that access the property.
    /// </summary>
    public ShaderStage stages { get; set; }

    /// <summary>
    /// Gets or sets the native serializable default value.
    /// </summary>
    public MaterialValue defaultValue { get; set; }

    /// <summary>
    /// Gets or sets how the property enters the shader resource interface.
    /// </summary>
    public ShaderPropertyBindingKind bindingKind { get; set; }

    /// <summary>
    /// Gets or sets required access for storage texture or buffer bindings.
    /// </summary>
    public RenderStorageAccess storageAccess { get; set; }

    internal static bool IsBindingKindCompatible(
        ShaderPropertyType type,
        ShaderPropertyBindingKind bindingKind)
        => bindingKind switch
        {
            ShaderPropertyBindingKind.Uniform => !IsTexture(type) && type != ShaderPropertyType.Buffer,
            ShaderPropertyBindingKind.SampledTexture => IsTexture(type),
            ShaderPropertyBindingKind.StorageTexture => IsTexture(type),
            ShaderPropertyBindingKind.StorageBuffer => type == ShaderPropertyType.Buffer,
            _ => false
        };

    private static ShaderPropertyBindingKind InferBindingKind(ShaderPropertyType type)
        => IsTexture(type)
            ? ShaderPropertyBindingKind.SampledTexture
            : type == ShaderPropertyType.Buffer
                ? ShaderPropertyBindingKind.StorageBuffer
                : ShaderPropertyBindingKind.Uniform;

    private static bool IsTexture(ShaderPropertyType type)
        => type is ShaderPropertyType.Texture2D
            or ShaderPropertyType.Texture2DArray
            or ShaderPropertyType.Texture3D
            or ShaderPropertyType.TextureCube;

    private static void ValidateBindingKind(
        ShaderPropertyType type,
        ShaderPropertyBindingKind bindingKind)
    {
        if (!IsBindingKindCompatible(type, bindingKind))
        {
            throw new ArgumentException(
                $"Shader property type '{type}' is incompatible with binding kind '{bindingKind}'.",
                nameof(bindingKind));
        }
    }
}

/// <summary>
/// Declares one static shader keyword that may produce compiled variants.
/// </summary>
public struct ShaderKeywordDefinition
{
    /// <summary>
    /// Creates a shader keyword definition.
    /// </summary>
    /// <param name="id">
    /// Stable keyword identifier.
    /// </param>
    /// <param name="options">
    /// Allowed stable option identifiers.
    /// </param>
    public ShaderKeywordDefinition(string id, IEnumerable<string> options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(options);
        this.id = id;
        this.options = options.ToArray();
    }

    /// <summary>
    /// Gets or sets the stable keyword identifier.
    /// </summary>
    public string id { get; set; }

    /// <summary>
    /// Gets or sets allowed stable option identifiers.
    /// </summary>
    public string[] options { get; set; }
}

/// <summary>
/// Represents shaderc-compatible source imported through the common asset system.
/// </summary>
[StableTypeId("80356429-c04e-4cf0-b32e-ebda7ed8d428")]
public sealed class ShaderSourceAsset : AssetObject
{
    internal ShaderSourceAsset()
    {
    }

    /// <summary>
    /// Creates an immutable imported shader-source description.
    /// </summary>
    /// <param name="content">
    /// The fully expanded shader source text.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="content"/> is <see langword="null"/>.
    /// </exception>
    public ShaderSourceAsset(string content)
    {
        this.content = content ?? throw new ArgumentNullException(nameof(content));
    }

    /// <summary>
    /// Gets or sets decoded source text.
    /// </summary>
    [SerializableProperty]
    public string content { get; internal set; } = string.Empty;
}

/// <summary>
/// Defines one backend-neutral raster or compute shader pass.
/// </summary>
public struct ShaderPassDefinition
{
    /// <summary>
    /// Creates a shader pass definition.
    /// </summary>
    /// <param name="name">
    /// Stable pass name within the shader.
    /// </param>
    /// <param name="programKind">
    /// Programmable stage combination.
    /// </param>
    /// <param name="vertexSource">
    /// Optional vertex source asset.
    /// </param>
    /// <param name="fragmentSource">
    /// Optional fragment source asset.
    /// </param>
    /// <param name="computeSource">
    /// Optional compute source asset.
    /// </param>
    /// <param name="varyingSource">
    /// Optional varying definition source asset.
    /// </param>
    /// <param name="requiredFeatures">
    /// Required device capability mask.
    /// </param>
    /// <param name="renderState">
    /// Backend-neutral fixed-function state.
    /// </param>
    /// <param name="metadata">
    /// Optional provider-defined metadata.
    /// </param>
    public ShaderPassDefinition(
        string name,
        ShaderProgramKind programKind,
        ShaderSourceAsset? vertexSource = null,
        ShaderSourceAsset? fragmentSource = null,
        ShaderSourceAsset? computeSource = null,
        ShaderSourceAsset? varyingSource = null,
        GraphicsFeature requiredFeatures = GraphicsFeature.None,
        ShaderRenderState? renderState = null,
        IEnumerable<ShaderMetadataEntry>? metadata = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        this.name = name;
        this.programKind = programKind;
        this.vertexSource = vertexSource;
        this.fragmentSource = fragmentSource;
        this.computeSource = computeSource;
        this.varyingSource = varyingSource;
        this.requiredFeatures = requiredFeatures;
        this.renderState = renderState ?? ShaderRenderState.opaque;
        this.metadata = metadata?.ToArray() ?? [];
    }

    /// <summary>
    /// Gets or sets the stable pass name.
    /// </summary>
    public string name { get; set; }

    /// <summary>
    /// Gets or sets the programmable stage combination.
    /// </summary>
    public ShaderProgramKind programKind { get; set; }

    /// <summary>
    /// Gets or sets the vertex source asset.
    /// </summary>
    public ShaderSourceAsset? vertexSource { get; set; }

    /// <summary>
    /// Gets or sets the fragment source asset.
    /// </summary>
    public ShaderSourceAsset? fragmentSource { get; set; }

    /// <summary>
    /// Gets or sets the compute source asset.
    /// </summary>
    public ShaderSourceAsset? computeSource { get; set; }

    /// <summary>
    /// Gets or sets the varying definition source asset.
    /// </summary>
    public ShaderSourceAsset? varyingSource { get; set; }

    /// <summary>
    /// Gets or sets required device capabilities.
    /// </summary>
    public GraphicsFeature requiredFeatures { get; set; }

    /// <summary>
    /// Gets or sets backend-neutral fixed-function state.
    /// </summary>
    public ShaderRenderState renderState { get; set; }

    /// <summary>
    /// Gets or sets provider-defined metadata.
    /// </summary>
    public ShaderMetadataEntry[] metadata { get; set; }
}

/// <summary>
/// Maps one provider-defined role to one concrete shader pass.
/// </summary>
public struct ShaderTechniquePass
{
    /// <summary>
    /// Creates a technique pass mapping.
    /// </summary>
    /// <param name="role">
    /// Open role defined by the technique contract.
    /// </param>
    /// <param name="passName">
    /// Concrete pass name within the shader.
    /// </param>
    public ShaderTechniquePass(ShaderPassRoleId role, string passName)
    {
        if (!role.isValid)
            throw new ArgumentException("A pass role must be valid.", nameof(role));
        ArgumentException.ThrowIfNullOrWhiteSpace(passName);
        this.role = role;
        this.passName = passName;
    }

    /// <summary>
    /// Gets or sets the provider-defined role.
    /// </summary>
    public ShaderPassRoleId role { get; set; }

    /// <summary>
    /// Gets or sets the concrete pass name.
    /// </summary>
    public string passName { get; set; }
}

/// <summary>
/// Declares one contract-compatible pass mapping selectable by materials.
/// </summary>
public struct ShaderTechniqueDefinition
{
    /// <summary>
    /// Creates a shader technique definition.
    /// </summary>
    /// <param name="id">
    /// Stable technique identifier.
    /// </param>
    /// <param name="contract">
    /// Open rendering-provider contract.
    /// </param>
    /// <param name="passes">
    /// Role-to-pass mappings.
    /// </param>
    /// <param name="requiredFeatures">
    /// Capabilities required by the complete technique.
    /// </param>
    public ShaderTechniqueDefinition(
        ShaderTechniqueId id,
        ShaderContractId contract,
        IEnumerable<ShaderTechniquePass> passes,
        GraphicsFeature requiredFeatures = GraphicsFeature.None)
    {
        if (!id.isValid)
            throw new ArgumentException("A technique ID must be valid.", nameof(id));
        if (!contract.isValid)
            throw new ArgumentException("A shader contract ID must be valid.", nameof(contract));
        ArgumentNullException.ThrowIfNull(passes);
        this.id = id;
        this.contract = contract;
        this.passes = passes.ToArray();
        this.requiredFeatures = requiredFeatures;
    }

    /// <summary>
    /// Gets or sets the stable technique identifier.
    /// </summary>
    public ShaderTechniqueId id { get; set; }

    /// <summary>
    /// Gets or sets the open rendering-provider contract.
    /// </summary>
    public ShaderContractId contract { get; set; }

    /// <summary>
    /// Gets or sets role-to-pass mappings.
    /// </summary>
    public ShaderTechniquePass[] passes { get; set; }

    /// <summary>
    /// Gets or sets capabilities required by the complete technique.
    /// </summary>
    public GraphicsFeature requiredFeatures { get; set; }
}

/// <summary>
/// Contains the source-of-truth definition shared by handwritten and graph shaders.
/// </summary>
public sealed class ShaderDefinition : ISerializable
{
    /// <summary>
    /// Creates an empty shader definition for native deserialization.
    /// </summary>
    public ShaderDefinition()
    {
    }

    /// <summary>
    /// Creates a shader definition.
    /// </summary>
    /// <param name="name">
    /// Artist-facing shader name.
    /// </param>
    /// <param name="properties">
    /// Stable property declarations.
    /// </param>
    /// <param name="keywords">
    /// Static keyword declarations.
    /// </param>
    /// <param name="passes">
    /// Backend-neutral pass declarations.
    /// </param>
    /// <param name="techniques">
    /// Open contract and role mappings.
    /// </param>
    public ShaderDefinition(
        string name,
        IEnumerable<ShaderPropertyDefinition> properties,
        IEnumerable<ShaderKeywordDefinition> keywords,
        IEnumerable<ShaderPassDefinition> passes,
        IEnumerable<ShaderTechniqueDefinition>? techniques = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(properties);
        ArgumentNullException.ThrowIfNull(keywords);
        ArgumentNullException.ThrowIfNull(passes);
        this.name = name;
        this.properties = properties.ToArray();
        this.keywords = keywords.ToArray();
        this.passes = passes.ToArray();
        this.techniques = techniques?.ToArray() ?? [];
    }

    /// <summary>
    /// Gets or sets the artist-facing shader name.
    /// </summary>
    [SerializableProperty]
    public string name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets stable property declarations.
    /// </summary>
    [SerializableProperty]
    public ShaderPropertyDefinition[] properties { get; set; } = [];

    /// <summary>
    /// Gets or sets static keyword declarations.
    /// </summary>
    [SerializableProperty]
    public ShaderKeywordDefinition[] keywords { get; set; } = [];

    /// <summary>
    /// Gets or sets backend-neutral pass declarations.
    /// </summary>
    [SerializableProperty]
    public ShaderPassDefinition[] passes { get; set; } = [];

    /// <summary>
    /// Gets or sets open contract and role mappings.
    /// </summary>
    [SerializableProperty]
    public ShaderTechniqueDefinition[] techniques { get; set; } = [];
}

/// <summary>
/// Represents an imported handwritten or generated shader definition.
/// </summary>
[StableTypeId("e6672287-145f-4f51-8380-a6aeaf57a801")]
public class ShaderAsset : AssetObject
{
    [SerializableProperty(PropertyVisibility.Hide)]
    private byte[] m_definitionData = [];

    [SerializableProperty(PropertyVisibility.Hide)]
    private ShaderSourceAsset[] m_sourceDependencies = [];

    /// <summary>
    /// Gets the currently committed backend-neutral definition.
    /// </summary>
    public ShaderDefinition? definition { get; private set; }

    /// <summary>
    /// Commits a validated definition through the native serialization channel.
    /// </summary>
    /// <param name="value">
    /// Complete definition visible to materials and target compilation.
    /// </param>
    /// <param name="serialization">
    /// The serialization registry that owns the active shader converter generation.
    /// </param>
    [ScriptingApiIgnore]
    public void SetDefinition(
        ShaderDefinition value,
        SerializationRegistry serialization)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(serialization);
        m_definitionData = serialization.Serialize(value);
        m_sourceDependencies = value.passes
            .SelectMany(static pass => new[]
            {
                pass.vertexSource,
                pass.fragmentSource,
                pass.computeSource,
                pass.varyingSource
            })
            .Where(static source => source is not null)
            .Cast<ShaderSourceAsset>()
            .Distinct()
            .ToArray();
        definition = value;
    }

    [OnSerializableRestored]
    private void OnSerializableRestored(SerializationContext context)
    {
        definition = m_definitionData.Length == 0
            ? null
            : context.GetRequired<SerializationRegistry>().Deserialize<ShaderDefinition>(
                m_definitionData,
                context);
    }
}

/// <summary>
/// Selects how texture samples are decoded for shader use.
/// </summary>
public enum TextureColorSpace
{
    /// <summary>
    /// Samples are interpreted as linear values.
    /// </summary>
    Linear,
    /// <summary>
    /// Color samples are decoded from sRGB at sampling time.
    /// </summary>
    Srgb
}

/// <summary>
/// Represents imported texture content without owning a GPU handle.
/// </summary>
[StableTypeId("e174b6eb-f79a-470f-a460-84f88ab49d0e")]
public sealed class TextureAsset : AssetObject
{
    internal TextureAsset()
    {
    }

    /// <summary>
    /// Creates an immutable imported texture description.
    /// </summary>
    /// <param name="width">
    /// The positive source pixel width.
    /// </param>
    /// <param name="height">
    /// The positive source pixel height.
    /// </param>
    /// <param name="colorSpace">
    /// The color-space interpretation used when sampling the texture.
    /// </param>
    /// <param name="sourceFormat">
    /// The normalized source container name without a leading period.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="width"/> or <paramref name="height"/> is not positive.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="sourceFormat"/> is empty or contains only white-space characters.
    /// </exception>
    public TextureAsset(
        int width,
        int height,
        TextureColorSpace colorSpace,
        string sourceFormat)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceFormat);
        this.width = width;
        this.height = height;
        this.colorSpace = colorSpace;
        this.sourceFormat = sourceFormat;
    }

    /// <summary>
    /// Gets the source pixel width.
    /// </summary>
    [SerializableProperty]
    public int width { get; internal set; }

    /// <summary>
    /// Gets the source pixel height.
    /// </summary>
    [SerializableProperty]
    public int height { get; internal set; }

    /// <summary>
    /// Gets the declared sample color space.
    /// </summary>
    [SerializableProperty]
    public TextureColorSpace colorSpace { get; internal set; }

    /// <summary>
    /// Gets the normalized source container name.
    /// </summary>
    [SerializableProperty]
    public string sourceFormat { get; internal set; } = string.Empty;
}

/// <summary>
/// Represents imported geometry without prescribing scene or draw semantics.
/// </summary>
[StableTypeId("f214637e-0a54-438d-8a72-9d892bd29a56")]
public sealed class GeometryAsset : AssetObject
{
    internal GeometryAsset()
    {
    }

    /// <summary>
    /// Creates an immutable imported geometry description.
    /// </summary>
    /// <param name="vertexCount">
    /// The number of normalized vertices.
    /// </param>
    /// <param name="indexCount">
    /// The number of indices.
    /// </param>
    /// <param name="sectionCount">
    /// The number of independently submitted sections.
    /// </param>
    /// <param name="boundsCenter">
    /// The object-space center of the geometry bounds.
    /// </param>
    /// <param name="boundsExtents">
    /// The non-negative object-space half-extents of the geometry bounds.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when any count is negative or a bounds extent is negative.
    /// </exception>
    public GeometryAsset(
        int vertexCount,
        int indexCount,
        int sectionCount,
        Vector3 boundsCenter,
        Vector3 boundsExtents)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(vertexCount);
        ArgumentOutOfRangeException.ThrowIfNegative(indexCount);
        ArgumentOutOfRangeException.ThrowIfNegative(sectionCount);
        if (boundsExtents.x < 0f || boundsExtents.y < 0f || boundsExtents.z < 0f)
            throw new ArgumentOutOfRangeException(nameof(boundsExtents), "Geometry bounds extents cannot be negative.");
        this.vertexCount = vertexCount;
        this.indexCount = indexCount;
        this.sectionCount = sectionCount;
        this.boundsCenter = boundsCenter;
        this.boundsExtents = boundsExtents;
    }

    /// <summary>
    /// Gets the number of normalized vertices.
    /// </summary>
    [SerializableProperty]
    public int vertexCount { get; internal set; }

    /// <summary>
    /// Gets the number of indices.
    /// </summary>
    [SerializableProperty]
    public int indexCount { get; internal set; }

    /// <summary>
    /// Gets the number of independently submitted geometry sections.
    /// </summary>
    [SerializableProperty]
    public int sectionCount { get; internal set; }

    /// <summary>
    /// Gets the object-space center of imported geometry bounds.
    /// </summary>
    [SerializableProperty]
    public Vector3 boundsCenter { get; internal set; }

    /// <summary>
    /// Gets the non-negative object-space half-extents of imported geometry bounds.
    /// </summary>
    [SerializableProperty]
    public Vector3 boundsExtents { get; internal set; }
}
