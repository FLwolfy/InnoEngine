using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Inno.Assets.Core;
using Inno.Core.Mathematics;
using Inno.Core.Reflection;
using Inno.Core.Serialization;

namespace Inno.Rendering;

/// <summary>
/// Identifies the neutral value stored by a material property.
/// </summary>
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

/// <summary>
/// Stores one backend-neutral material value without a GPU binding.
/// </summary>
public readonly record struct MaterialValue
{
    private MaterialValue(
        MaterialValueKind kind,
        Vector4 vector,
        Matrix matrix,
        TextureAsset? texture)
    {
        this.kind = kind;
        this.vector = vector;
        this.matrix = matrix;
        this.texture = texture;
    }

    /// <summary>Gets the stored value kind.</summary>
    public MaterialValueKind kind { get; }

    /// <summary>Gets scalar, vector or color components.</summary>
    public Vector4 vector { get; }

    /// <summary>Gets the matrix value when <see cref="kind"/> is <see cref="MaterialValueKind.Matrix"/>.</summary>
    public Matrix matrix { get; }

    /// <summary>Gets the texture reference when <see cref="kind"/> is <see cref="MaterialValueKind.Texture"/>.</summary>
    public TextureAsset? texture { get; }

    /// <summary>Creates a scalar material value.</summary>
    /// <param name="value">Scalar value.</param>
    /// <returns>A scalar material value.</returns>
    public static MaterialValue FromFloat(float value)
        => new(MaterialValueKind.Float, new Vector4(value, 0f, 0f, 0f), default, null);

    /// <summary>Creates a vector material value.</summary>
    /// <param name="value">Vector value.</param>
    /// <returns>A vector material value.</returns>
    public static MaterialValue FromVector(Vector4 value)
        => new(MaterialValueKind.Vector, value, default, null);

    /// <summary>Creates a linear color material value.</summary>
    /// <param name="value">Color value.</param>
    /// <returns>A color material value.</returns>
    public static MaterialValue FromColor(Color value)
        => new(MaterialValueKind.Color, new Vector4(value.r, value.g, value.b, value.a), default, null);

    /// <summary>Creates a matrix material value.</summary>
    /// <param name="value">Four-by-four matrix value.</param>
    /// <returns>A matrix material value.</returns>
    public static MaterialValue FromMatrix(Matrix value)
        => new(MaterialValueKind.Matrix, default, value, null);

    /// <summary>Creates a texture material value.</summary>
    /// <param name="value">Texture asset reference.</param>
    /// <returns>A texture material value.</returns>
    public static MaterialValue FromTexture(TextureAsset value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new MaterialValue(MaterialValueKind.Texture, default, default, value);
    }
}

/// <summary>
/// Stores one persistent material property entry.
/// </summary>
public sealed class MaterialPropertyEntry
{
    /// <summary>
    /// Creates a persistent material property entry.
    /// </summary>
    /// <param name="id">Stable shader property identifier.</param>
    /// <param name="value">Neutral material value.</param>
    public MaterialPropertyEntry(ShaderPropertyId id, MaterialValue value)
    {
        this.id = id;
        this.value = value;
    }

    /// <summary>Gets the stable shader property identifier.</summary>
    public ShaderPropertyId id { get; }

    /// <summary>Gets or sets the neutral material value.</summary>
    public MaterialValue value { get; set; }
}

/// <summary>
/// Represents imported material state keyed by stable shader property identifiers.
/// </summary>
[StableTypeId("56f1fdc7-dad9-464a-848f-fcae4c33ecf2")]
public sealed class MaterialAsset : AssetObject
{
    private readonly List<MaterialPropertyEntry> m_properties = [];
    private readonly HashSet<string> m_keywords = new(StringComparer.Ordinal);
    [SerializableProperty(PropertyVisibility.Hide)]
    private string m_propertyStateJson = "[]";
    [SerializableProperty(PropertyVisibility.Hide)]
    private TextureAsset?[] m_textureDependencies = [];
    [SerializableProperty(PropertyVisibility.Hide)]
    private string[] m_keywordState = [];

    /// <summary>Gets or sets the referenced shader asset.</summary>
    [SerializableProperty]
    public ShaderAsset? shader { get; set; }

    /// <summary>Gets persistent material values in stable insertion order.</summary>
    public IReadOnlyList<MaterialPropertyEntry> properties => m_properties;

    /// <summary>Gets enabled stable keyword option identifiers.</summary>
    public IReadOnlySet<string> keywords => m_keywords;

    /// <summary>Gets or sets an optional render queue override.</summary>
    [SerializableProperty]
    public int? renderQueue { get; set; }

    /// <summary>Creates or replaces one material property.</summary>
    /// <param name="id">Stable shader property identifier.</param>
    /// <param name="value">Neutral material value.</param>
    public void Set(ShaderPropertyId id, MaterialValue value)
    {
        MaterialPropertyEntry? entry = m_properties.Find(candidate => candidate.id == id);
        if (entry is null)
        {
            m_properties.Add(new MaterialPropertyEntry(id, value));
        }
        else
        {
            entry.value = value;
        }

        SynchronizeState();
    }

    /// <summary>Tries to read one material property.</summary>
    /// <param name="id">Stable shader property identifier.</param>
    /// <param name="value">Receives the neutral value when present.</param>
    /// <returns><see langword="true"/> when the property exists.</returns>
    public bool TryGet(ShaderPropertyId id, out MaterialValue value)
    {
        MaterialPropertyEntry? entry = m_properties.Find(candidate => candidate.id == id);
        value = entry?.value ?? default;
        return entry is not null;
    }

    /// <summary>Enables or disables a declared static keyword option.</summary>
    /// <param name="keyword">Stable keyword option identifier.</param>
    /// <param name="enabled">Whether the option should be enabled.</param>
    public void SetKeyword(string keyword, bool enabled)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyword);
        if (enabled)
        {
            m_keywords.Add(keyword);
        }
        else
        {
            m_keywords.Remove(keyword);
        }

        m_keywordState = m_keywords.OrderBy(static value => value, StringComparer.Ordinal).ToArray();
    }

    [OnSerializableRestored]
    private void OnSerializableRestored()
    {
        m_properties.Clear();
        MaterialPropertyData[] values = JsonSerializer.Deserialize<MaterialPropertyData[]>(m_propertyStateJson)
            ?? [];
        foreach (MaterialPropertyData value in values)
        {
            TextureAsset? texture = value.textureIndex >= 0
                && value.textureIndex < m_textureDependencies.Length
                    ? m_textureDependencies[value.textureIndex]
                    : null;
            MaterialValue materialValue = value.kind switch
            {
                MaterialValueKind.Float => MaterialValue.FromFloat(value.x),
                MaterialValueKind.Vector => MaterialValue.FromVector(
                    new Vector4(value.x, value.y, value.z, value.w)),
                MaterialValueKind.Color => MaterialValue.FromColor(
                    new Color(value.x, value.y, value.z, value.w)),
                MaterialValueKind.Matrix => MaterialValue.FromMatrix(new Matrix(
                    value.m11, value.m12, value.m13, value.m14,
                    value.m21, value.m22, value.m23, value.m24,
                    value.m31, value.m32, value.m33, value.m34,
                    value.m41, value.m42, value.m43, value.m44)),
                MaterialValueKind.Texture when texture is not null => MaterialValue.FromTexture(texture),
                MaterialValueKind.Texture => throw new InvalidOperationException(
                    $"Material texture property '{value.id}' has no dependency."),
                _ => throw new InvalidOperationException($"Unsupported material value kind '{value.kind}'.")
            };
            m_properties.Add(new MaterialPropertyEntry(new ShaderPropertyId(value.id), materialValue));
        }

        m_keywords.Clear();
        foreach (string keyword in m_keywordState)
        {
            m_keywords.Add(keyword);
        }
    }

    private void SynchronizeState()
    {
        var textures = new List<TextureAsset>();
        var values = new List<MaterialPropertyData>(m_properties.Count);
        foreach (MaterialPropertyEntry entry in m_properties)
        {
            int textureIndex = -1;
            if (entry.value.texture is not null)
            {
                textureIndex = textures.IndexOf(entry.value.texture);
                if (textureIndex < 0)
                {
                    textureIndex = textures.Count;
                    textures.Add(entry.value.texture);
                }
            }

            values.Add(new MaterialPropertyData
            {
                id = entry.id.value,
                kind = entry.value.kind,
                x = entry.value.vector.x,
                y = entry.value.vector.y,
                z = entry.value.vector.z,
                w = entry.value.vector.w,
                m11 = entry.value.matrix.m11,
                m12 = entry.value.matrix.m12,
                m13 = entry.value.matrix.m13,
                m14 = entry.value.matrix.m14,
                m21 = entry.value.matrix.m21,
                m22 = entry.value.matrix.m22,
                m23 = entry.value.matrix.m23,
                m24 = entry.value.matrix.m24,
                m31 = entry.value.matrix.m31,
                m32 = entry.value.matrix.m32,
                m33 = entry.value.matrix.m33,
                m34 = entry.value.matrix.m34,
                m41 = entry.value.matrix.m41,
                m42 = entry.value.matrix.m42,
                m43 = entry.value.matrix.m43,
                m44 = entry.value.matrix.m44,
                textureIndex = textureIndex
            });
        }

        m_textureDependencies = [.. textures];
        m_propertyStateJson = JsonSerializer.Serialize(values);
    }

    private sealed class MaterialPropertyData
    {
        public string id { get; set; } = string.Empty;
        public MaterialValueKind kind { get; set; }
        public float x { get; set; }
        public float y { get; set; }
        public float z { get; set; }
        public float w { get; set; }
        public float m11 { get; set; }
        public float m12 { get; set; }
        public float m13 { get; set; }
        public float m14 { get; set; }
        public float m21 { get; set; }
        public float m22 { get; set; }
        public float m23 { get; set; }
        public float m24 { get; set; }
        public float m31 { get; set; }
        public float m32 { get; set; }
        public float m33 { get; set; }
        public float m34 { get; set; }
        public float m41 { get; set; }
        public float m42 { get; set; }
        public float m43 { get; set; }
        public float m44 { get; set; }
        public int textureIndex { get; set; } = -1;
    }
}

/// <summary>
/// Holds per-renderer material overrides without modifying shared material assets.
/// </summary>
public sealed class MaterialPropertyBlock
{
    private readonly Dictionary<ShaderPropertyId, MaterialValue> m_values = [];

    /// <summary>Gets the number of active overrides.</summary>
    public int count => m_values.Count;

    /// <summary>Creates or replaces one per-renderer override.</summary>
    /// <param name="id">Stable shader property identifier.</param>
    /// <param name="value">Neutral material value.</param>
    public void Set(ShaderPropertyId id, MaterialValue value) => m_values[id] = value;

    /// <summary>Tries to read one per-renderer override.</summary>
    /// <param name="id">Stable shader property identifier.</param>
    /// <param name="value">Receives the neutral value when present.</param>
    /// <returns><see langword="true"/> when the override exists.</returns>
    public bool TryGet(ShaderPropertyId id, out MaterialValue value) => m_values.TryGetValue(id, out value);

    /// <summary>Removes all per-renderer overrides.</summary>
    public void Clear() => m_values.Clear();

    internal MaterialPropertyBlock Snapshot()
    {
        var snapshot = new MaterialPropertyBlock();
        foreach ((ShaderPropertyId id, MaterialValue value) in m_values)
        {
            snapshot.m_values.Add(id, value);
        }

        return snapshot;
    }
}
