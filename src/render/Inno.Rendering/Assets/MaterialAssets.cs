using System;
using System.Collections.Generic;
using System.Linq;
using Inno.Assets.Core;
using Inno.Core.Mathematics;
using Inno.Core.Reflection;
using Inno.Core.Serialization;
using Inno.Rendering.Core;

namespace Inno.Rendering;

/// <summary>Identifies the neutral value stored by a material property.</summary>
public enum MaterialValueKind
{
    /// <summary>Scalar floating-point value.</summary>
    Float,
    /// <summary>Four-component vector value.</summary>
    Vector,
    /// <summary>Linear color value.</summary>
    Color,
    /// <summary>Four-by-four matrix value.</summary>
    Matrix,
    /// <summary>Texture asset reference.</summary>
    Texture
}

/// <summary>Stores one native-serializable material value without a GPU binding.</summary>
public struct MaterialValue
{
    private MaterialValue(
        MaterialValueKind kind,
        Vector4 vector,
        Matrix matrix,
        TextureAsset? texture,
        RenderSamplerState sampler)
    {
        this.kind = kind;
        this.vector = vector;
        this.matrix = matrix;
        this.texture = texture;
        this.sampler = sampler;
    }

    /// <summary>Gets or sets the stored value kind.</summary>
    public MaterialValueKind kind { get; set; }

    /// <summary>Gets or sets scalar, vector, or color components.</summary>
    public Vector4 vector { get; set; }

    /// <summary>Gets or sets the matrix value.</summary>
    public Matrix matrix { get; set; }

    /// <summary>Gets or sets the texture reference.</summary>
    public TextureAsset? texture { get; set; }

    /// <summary>Gets or sets the sampler used when this value stores a texture.</summary>
    public RenderSamplerState sampler { get; set; }

    /// <summary>Creates a scalar material value.</summary>
    /// <param name="value">Scalar value.</param>
    /// <returns>A scalar material value.</returns>
    public static MaterialValue FromFloat(float value)
        => new(MaterialValueKind.Float, new Vector4(value, 0f, 0f, 0f), default, null, default);

    /// <summary>Creates a vector material value.</summary>
    /// <param name="value">Vector value.</param>
    /// <returns>A vector material value.</returns>
    public static MaterialValue FromVector(Vector4 value)
        => new(MaterialValueKind.Vector, value, default, null, default);

    /// <summary>Creates a linear color material value.</summary>
    /// <param name="value">Color value.</param>
    /// <returns>A color material value.</returns>
    public static MaterialValue FromColor(Color value)
        => new(MaterialValueKind.Color, new Vector4(value.r, value.g, value.b, value.a), default, null, default);

    /// <summary>Creates a matrix material value.</summary>
    /// <param name="value">Four-by-four matrix value.</param>
    /// <returns>A matrix material value.</returns>
    public static MaterialValue FromMatrix(Matrix value)
        => new(MaterialValueKind.Matrix, default, value, null, default);

    /// <summary>Creates a texture material value.</summary>
    /// <param name="value">Texture asset reference.</param>
    /// <param name="sampler">Optional sampler state; linear clamp is used when omitted.</param>
    /// <returns>A texture material value.</returns>
    public static MaterialValue FromTexture(
        TextureAsset value,
        RenderSamplerState? sampler = null)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new MaterialValue(
            MaterialValueKind.Texture,
            default,
            default,
            value,
            sampler ?? RenderSamplerState.linearClamp);
    }
}

/// <summary>Stores one persistent material property entry.</summary>
public struct MaterialPropertyEntry
{
    /// <summary>Creates a persistent material property entry.</summary>
    /// <param name="id">Stable shader property identifier.</param>
    /// <param name="value">Neutral material value.</param>
    public MaterialPropertyEntry(ShaderPropertyId id, MaterialValue value)
    {
        if (!id.isValid)
            throw new ArgumentException("A material property ID must be valid.", nameof(id));
        this.id = id;
        this.value = value;
    }

    /// <summary>Gets or sets the stable shader property identifier.</summary>
    public ShaderPropertyId id { get; set; }

    /// <summary>Gets or sets the neutral material value.</summary>
    public MaterialValue value { get; set; }
}

/// <summary>Stores one open material metadata key and value.</summary>
public struct MaterialMetadataEntry
{
    /// <summary>Creates a material metadata entry.</summary>
    /// <param name="key">Stable provider-defined key.</param>
    /// <param name="value">Provider-defined value.</param>
    public MaterialMetadataEntry(string key, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        this.key = key;
        this.value = value ?? string.Empty;
    }

    /// <summary>Gets or sets the stable metadata key.</summary>
    public string key { get; set; }

    /// <summary>Gets or sets the metadata value.</summary>
    public string value { get; set; }
}

/// <summary>Represents material state keyed by stable shader property identifiers.</summary>
[StableTypeId("56f1fdc7-dad9-464a-848f-fcae4c33ecf2")]
public sealed class MaterialAsset : AssetObject
{
    [SerializableProperty(PropertyVisibility.Hide)]
    private MaterialPropertyEntry[] m_properties = [];

    [SerializableProperty(PropertyVisibility.Hide)]
    private string[] m_keywords = [];

    [SerializableProperty(PropertyVisibility.Hide)]
    private MaterialMetadataEntry[] m_metadata = [];

    /// <summary>Gets or sets the referenced shader asset.</summary>
    [SerializableProperty]
    public ShaderAsset? shader { get; set; }

    /// <summary>Gets or sets an optional explicitly selected technique.</summary>
    [SerializableProperty]
    public ShaderTechniqueId techniqueId { get; set; }

    /// <summary>Gets persistent material values in stable insertion order.</summary>
    public IReadOnlyList<MaterialPropertyEntry> properties => m_properties;

    /// <summary>Gets enabled stable keyword option identifiers.</summary>
    public IReadOnlyList<string> keywords => m_keywords;

    /// <summary>Gets open provider-defined metadata.</summary>
    public IReadOnlyList<MaterialMetadataEntry> metadata => m_metadata;

    /// <summary>Creates or replaces one material property.</summary>
    /// <param name="id">Stable shader property identifier.</param>
    /// <param name="value">Neutral material value.</param>
    public void Set(ShaderPropertyId id, MaterialValue value)
    {
        if (!id.isValid)
            throw new ArgumentException("A material property ID must be valid.", nameof(id));
        int index = Array.FindIndex(m_properties, candidate => candidate.id == id);
        if (index < 0)
        {
            Array.Resize(ref m_properties, m_properties.Length + 1);
            m_properties[^1] = new MaterialPropertyEntry(id, value);
            return;
        }

        m_properties[index] = new MaterialPropertyEntry(id, value);
    }

    /// <summary>Tries to read one material property.</summary>
    /// <param name="id">Stable shader property identifier.</param>
    /// <param name="value">Receives the neutral value when present.</param>
    /// <returns><see langword="true"/> when the property exists.</returns>
    public bool TryGet(ShaderPropertyId id, out MaterialValue value)
    {
        int index = Array.FindIndex(m_properties, candidate => candidate.id == id);
        value = index < 0 ? default : m_properties[index].value;
        return index >= 0;
    }

    /// <summary>Enables or disables a declared static keyword option.</summary>
    /// <param name="keyword">Stable keyword option identifier.</param>
    /// <param name="enabled">Whether the option should be enabled.</param>
    public void SetKeyword(string keyword, bool enabled)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyword);
        var values = m_keywords.ToHashSet(StringComparer.Ordinal);
        if (enabled)
            values.Add(keyword);
        else
            values.Remove(keyword);
        m_keywords = values.OrderBy(static value => value, StringComparer.Ordinal).ToArray();
    }

    /// <summary>Creates or replaces one provider-defined metadata value.</summary>
    /// <param name="key">Stable metadata key.</param>
    /// <param name="value">Provider-defined value.</param>
    public void SetMetadata(string key, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        int index = Array.FindIndex(m_metadata, entry => string.Equals(entry.key, key, StringComparison.Ordinal));
        if (index < 0)
        {
            Array.Resize(ref m_metadata, m_metadata.Length + 1);
            m_metadata[^1] = new MaterialMetadataEntry(key, value);
            return;
        }

        m_metadata[index] = new MaterialMetadataEntry(key, value);
    }

    /// <summary>Tries to read one provider-defined metadata value.</summary>
    /// <param name="key">Stable metadata key.</param>
    /// <param name="value">Receives the metadata value when present.</param>
    /// <returns><see langword="true"/> when the key exists.</returns>
    public bool TryGetMetadata(string key, out string? value)
    {
        int index = Array.FindIndex(m_metadata, entry => string.Equals(entry.key, key, StringComparison.Ordinal));
        value = index < 0 ? null : m_metadata[index].value;
        return index >= 0;
    }
}

/// <summary>Holds frame-local material overrides without modifying a shared asset.</summary>
public sealed class MaterialPropertyBlock
{
    private readonly Dictionary<ShaderPropertyId, MaterialValue> m_values = [];

    /// <summary>Gets the number of active overrides.</summary>
    public int count => m_values.Count;

    /// <summary>Creates or replaces one frame-local override.</summary>
    /// <param name="id">Stable shader property identifier.</param>
    /// <param name="value">Neutral material value.</param>
    public void Set(ShaderPropertyId id, MaterialValue value)
    {
        if (!id.isValid)
            throw new ArgumentException("A material property ID must be valid.", nameof(id));
        m_values[id] = value;
    }

    /// <summary>Tries to read one frame-local override.</summary>
    /// <param name="id">Stable shader property identifier.</param>
    /// <param name="value">Receives the neutral value when present.</param>
    /// <returns><see langword="true"/> when the override exists.</returns>
    public bool TryGet(ShaderPropertyId id, out MaterialValue value) => m_values.TryGetValue(id, out value);

    /// <summary>Removes all frame-local overrides.</summary>
    public void Clear() => m_values.Clear();

    internal MaterialPropertyBlock Snapshot()
    {
        var snapshot = new MaterialPropertyBlock();
        foreach ((ShaderPropertyId id, MaterialValue value) in m_values)
            snapshot.m_values.Add(id, value);
        return snapshot;
    }
}

/// <summary>Describes one capability-compatible material pass selection.</summary>
public sealed class MaterialPassResolution
{
    /// <summary>Creates a material pass resolution.</summary>
    /// <param name="technique">Selected open-contract technique.</param>
    /// <param name="pass">Concrete pass mapped to the requested role.</param>
    public MaterialPassResolution(ShaderTechniqueDefinition technique, ShaderPassDefinition pass)
    {
        this.technique = technique;
        this.pass = pass;
    }

    /// <summary>Gets the selected technique.</summary>
    public ShaderTechniqueDefinition technique { get; }

    /// <summary>Gets the concrete shader pass.</summary>
    public ShaderPassDefinition pass { get; }
}

/// <summary>Resolves materials through open provider-owned shader contracts and roles.</summary>
public static class MaterialPassResolver
{
    /// <summary>Resolves one capability-compatible pass.</summary>
    /// <param name="material">Material whose shader and technique should be queried.</param>
    /// <param name="contractId">Rendering-provider contract required by the caller.</param>
    /// <param name="passRoleId">Provider-defined pass role required by the caller.</param>
    /// <param name="capabilities">Current device capability snapshot.</param>
    /// <returns>The selected technique and pass, or <see langword="null"/> when no compatible mapping exists.</returns>
    public static MaterialPassResolution? Resolve(
        MaterialAsset material,
        ShaderContractId contractId,
        ShaderPassRoleId passRoleId,
        GraphicsCapabilities capabilities)
    {
        ArgumentNullException.ThrowIfNull(material);
        ArgumentNullException.ThrowIfNull(capabilities);
        if (!contractId.isValid)
            throw new ArgumentException("A shader contract ID must be valid.", nameof(contractId));
        if (!passRoleId.isValid)
            throw new ArgumentException("A shader pass role ID must be valid.", nameof(passRoleId));

        ShaderDefinition? definition = material.shader?.definition;
        if (definition is null)
            return null;

        IEnumerable<ShaderTechniqueDefinition> candidates = definition.techniques.Where(technique =>
            technique.contract == contractId
            && capabilities.Supports(technique.requiredFeatures));
        if (material.techniqueId.isValid)
            candidates = candidates.Where(technique => technique.id == material.techniqueId);

        foreach (ShaderTechniqueDefinition technique in candidates)
        {
            ShaderTechniquePass mapping = technique.passes.FirstOrDefault(value => value.role == passRoleId);
            if (string.IsNullOrWhiteSpace(mapping.passName))
                continue;
            int passIndex = Array.FindIndex(definition.passes, pass =>
                string.Equals(pass.name, mapping.passName, StringComparison.Ordinal)
                && capabilities.Supports(pass.requiredFeatures));
            if (passIndex >= 0)
                return new MaterialPassResolution(technique, definition.passes[passIndex]);
        }

        return null;
    }
}
