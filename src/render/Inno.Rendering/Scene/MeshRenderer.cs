using System;
using System.Collections.Generic;
using Inno.Core.Reflection;
using Inno.Core.Serialization;
using Inno.Engine.Scene;

namespace Inno.Rendering;

/// <summary>
/// Associates imported mesh geometry with ordered shared materials.
/// </summary>
[StableTypeId("ca55cf6e-a7da-42d0-8310-5e4d5c453f90")]
public sealed class MeshRenderer : GameComponent
{
    private readonly List<MaterialAsset> m_materials = [];
    private MaterialPropertyBlock? m_propertyBlock;

    /// <summary>Gets or sets imported mesh geometry.</summary>
    [SerializableProperty]
    public MeshAsset? mesh { get; set; }

    /// <summary>Gets ordered shared materials by submesh slot.</summary>
    public IReadOnlyList<MaterialAsset> materials => m_materials;

    [SerializableProperty(PropertyVisibility.Hide)]
    private MaterialAsset[] materialSlots
    {
        get => [.. m_materials];
        set
        {
            m_materials.Clear();
            if (value is null)
                return;
            foreach (MaterialAsset material in value)
            {
                if (material is not null)
                    m_materials.Add(material);
            }
        }
    }

    /// <summary>Gets or sets directional shadow casting behavior.</summary>
    [SerializableProperty]
    public ShadowCastingMode shadowCastingMode { get; set; } = ShadowCastingMode.On;

    /// <summary>Gets or sets whether lighting may sample received shadows.</summary>
    [SerializableProperty]
    public bool receiveShadows { get; set; } = true;

    /// <summary>Gets or sets whether compatible draws may be instanced.</summary>
    [SerializableProperty]
    public bool enableInstancing { get; set; } = true;

    /// <summary>
    /// Replaces one shared material slot.
    /// </summary>
    /// <param name="index">Zero-based submesh material index.</param>
    /// <param name="material">Shared material asset.</param>
    public void SetMaterial(int index, MaterialAsset material)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentNullException.ThrowIfNull(material);
        while (m_materials.Count <= index)
        {
            m_materials.Add(material);
        }

        m_materials[index] = material;
    }

    /// <summary>
    /// Replaces all shared materials in submesh order.
    /// </summary>
    /// <param name="materials">Ordered non-null material assets.</param>
    public void SetMaterials(IEnumerable<MaterialAsset> materials)
    {
        ArgumentNullException.ThrowIfNull(materials);
        m_materials.Clear();
        foreach (MaterialAsset material in materials)
        {
            ArgumentNullException.ThrowIfNull(material);
            m_materials.Add(material);
        }
    }

    /// <summary>
    /// Copies references to the current per-renderer property block.
    /// </summary>
    /// <param name="propertyBlock">Property block to use, or <see langword="null"/> to clear overrides.</param>
    public void SetPropertyBlock(MaterialPropertyBlock? propertyBlock) => m_propertyBlock = propertyBlock;

    /// <summary>
    /// Gets the current per-renderer property block.
    /// </summary>
    /// <returns>The current block, or <see langword="null"/> when no overrides exist.</returns>
    public MaterialPropertyBlock? GetPropertyBlock() => m_propertyBlock;

    /// <inheritdoc />
    protected override void Reset()
    {
        mesh = null;
        m_materials.Clear();
        shadowCastingMode = ShadowCastingMode.On;
        receiveShadows = true;
        enableInstancing = true;
        m_propertyBlock = null;
    }
}
