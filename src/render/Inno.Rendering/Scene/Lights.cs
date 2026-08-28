using System;
using Inno.Core.Mathematics;
using Inno.Core.Reflection;
using Inno.Core.Serialization;
using Inno.Engine.Scene;

namespace Inno.Rendering;

/// <summary>
/// Stores color, intensity and shared shadow settings for a light component.
/// </summary>
public abstract class Light : GameComponent
{
    private float m_intensity = 1f;
    private float m_shadowStrength = 1f;

    /// <summary>Gets or sets the linear light color.</summary>
    [SerializableProperty]
    public Color color { get; set; } = Color.WHITE;

    /// <summary>Gets or sets non-negative radiometric intensity in pipeline-defined units.</summary>
    [SerializableProperty]
    public float intensity
    {
        get => m_intensity;
        set => m_intensity = Math.Max(0f, value);
    }

    /// <summary>Gets or sets whether this light requests supported shadow rendering.</summary>
    [SerializableProperty]
    public bool shadows { get; set; }

    /// <summary>Gets or sets normalized shadow contribution.</summary>
    [SerializableProperty]
    public float shadowStrength
    {
        get => m_shadowStrength;
        set => m_shadowStrength = Math.Clamp(value, 0f, 1f);
    }
}

/// <summary>
/// Emits parallel light rays and optionally owns cascaded directional shadows.
/// </summary>
[StableTypeId("f1b8bd78-76cf-447d-ae3f-a91ee1726d10")]
public sealed class DirectionalLight : Light
{
    private int m_shadowCascadeCount = 4;

    /// <summary>Gets or sets the cascade count from one through four.</summary>
    [SerializableProperty]
    public int shadowCascadeCount
    {
        get => m_shadowCascadeCount;
        set => m_shadowCascadeCount = Math.Clamp(value, 1, 4);
    }

    /// <inheritdoc />
    protected override void Reset()
    {
        color = Color.WHITE;
        intensity = 1f;
        shadows = true;
        shadowStrength = 1f;
        m_shadowCascadeCount = 4;
    }
}

/// <summary>
/// Emits omnidirectional light from one world-space position.
/// </summary>
[StableTypeId("59d7ea9c-e269-44b8-902a-a643617e32a2")]
public sealed class PointLight : Light
{
    private float m_range = 10f;

    /// <summary>Gets or sets the positive influence radius.</summary>
    [SerializableProperty]
    public float range
    {
        get => m_range;
        set => m_range = Math.Max(0.0001f, value);
    }

    /// <inheritdoc />
    protected override void Reset()
    {
        color = Color.WHITE;
        intensity = 1f;
        shadows = false;
        shadowStrength = 1f;
        m_range = 10f;
    }
}

/// <summary>
/// Emits a conical local light from one world-space position and direction.
/// </summary>
[StableTypeId("1a9a687b-d773-42f4-9581-bfed7f80ba33")]
public sealed class SpotLight : Light
{
    private float m_range = 10f;
    private float m_innerAngle = 25f;
    private float m_outerAngle = 35f;

    /// <summary>Gets or sets the positive influence distance.</summary>
    [SerializableProperty]
    public float range
    {
        get => m_range;
        set => m_range = Math.Max(0.0001f, value);
    }

    /// <summary>Gets or sets the inner cone angle in degrees.</summary>
    [SerializableProperty]
    public float innerAngle
    {
        get => m_innerAngle;
        set
        {
            m_innerAngle = Math.Clamp(value, 0f, 179f);
            m_outerAngle = Math.Max(m_outerAngle, m_innerAngle);
        }
    }

    /// <summary>Gets or sets the outer cone angle in degrees.</summary>
    [SerializableProperty]
    public float outerAngle
    {
        get => m_outerAngle;
        set => m_outerAngle = Math.Clamp(value, m_innerAngle, 179f);
    }

    /// <inheritdoc />
    protected override void Reset()
    {
        color = Color.WHITE;
        intensity = 1f;
        shadows = false;
        shadowStrength = 1f;
        m_range = 10f;
        m_innerAngle = 25f;
        m_outerAngle = 35f;
    }
}
