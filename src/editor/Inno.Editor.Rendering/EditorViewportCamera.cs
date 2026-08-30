using System;

using Inno.Core.Mathematics;

namespace Inno.Editor.Rendering;

/// <summary>
/// Stores rendering-model-neutral navigation state for one Editor viewport.
/// </summary>
/// <remarks>
/// A viewport provider maps this state to its own camera model. The host owns the instance so camera
/// navigation survives provider reloads without retaining Plugin types or delegates.
/// </remarks>
public sealed class EditorViewportCamera
{
    private const float C_MINIMUM_ORTHOGRAPHIC_SIZE = 0.001f;
    private const float C_MINIMUM_FIELD_OF_VIEW = 1f;
    private const float C_MAXIMUM_FIELD_OF_VIEW = 179f;

    private Vector3 m_position;
    private Quaternion m_rotation = Quaternion.identity;
    private float m_orthographicSize = 5f;
    private float m_fieldOfView = 60f;

    /// <summary>Gets whether a provider or restored panel state has initialized this camera.</summary>
    public bool isInitialized { get; private set; }

    /// <summary>Gets or sets the provider-defined world-space camera position.</summary>
    /// <exception cref="ArgumentException">Thrown when any component is not finite.</exception>
    public Vector3 position
    {
        get => m_position;
        set
        {
            ValidateFinite(value, nameof(value));
            m_position = value;
            isInitialized = true;
        }
    }

    /// <summary>Gets or sets the normalized provider-defined world-space camera rotation.</summary>
    /// <exception cref="ArgumentException">
    /// Thrown when any component is not finite or the quaternion has no usable magnitude.
    /// </exception>
    public Quaternion rotation
    {
        get => m_rotation;
        set
        {
            ValidateFinite(value, nameof(value));
            if (value.LengthSquared() < 0.000001f)
                throw new ArgumentException("Camera rotation must have a usable magnitude.", nameof(value));
            m_rotation = value.normalized;
            isInitialized = true;
        }
    }

    /// <summary>Gets or sets whether the provider should use an orthographic projection.</summary>
    public bool isOrthographic { get; set; } = true;

    /// <summary>Gets or sets the positive orthographic half-height in provider world units.</summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when the value is not finite or is smaller than the supported positive minimum.
    /// </exception>
    public float orthographicSize
    {
        get => m_orthographicSize;
        set
        {
            if (!float.IsFinite(value) || value < C_MINIMUM_ORTHOGRAPHIC_SIZE)
                throw new ArgumentOutOfRangeException(nameof(value));
            m_orthographicSize = value;
            isInitialized = true;
        }
    }

    /// <summary>Gets or sets the perspective vertical field of view in degrees.</summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when the value is not finite or lies outside the open projection range.
    /// </exception>
    public float fieldOfView
    {
        get => m_fieldOfView;
        set
        {
            if (!float.IsFinite(value) || value < C_MINIMUM_FIELD_OF_VIEW || value > C_MAXIMUM_FIELD_OF_VIEW)
                throw new ArgumentOutOfRangeException(nameof(value));
            m_fieldOfView = value;
            isInitialized = true;
        }
    }

    /// <summary>Atomically initializes the state for an orthographic provider.</summary>
    /// <param name="position">Provider-defined world-space camera position.</param>
    /// <param name="rotation">Provider-defined world-space camera rotation.</param>
    /// <param name="orthographicSize">Positive orthographic half-height in world units.</param>
    /// <exception cref="ArgumentException">
    /// Thrown when position or rotation contains non-finite values, or rotation has no usable magnitude.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="orthographicSize"/> is invalid.
    /// </exception>
    public void ConfigureOrthographic(
        Vector3 position,
        Quaternion rotation,
        float orthographicSize)
    {
        this.position = position;
        this.rotation = rotation;
        this.orthographicSize = orthographicSize;
        isOrthographic = true;
    }

    private static void ValidateFinite(Vector3 value, string parameterName)
    {
        if (!float.IsFinite(value.x) || !float.IsFinite(value.y) || !float.IsFinite(value.z))
            throw new ArgumentException("Camera position components must be finite.", parameterName);
    }

    private static void ValidateFinite(Quaternion value, string parameterName)
    {
        if (!float.IsFinite(value.x)
            || !float.IsFinite(value.y)
            || !float.IsFinite(value.z)
            || !float.IsFinite(value.w))
        {
            throw new ArgumentException("Camera rotation components must be finite.", parameterName);
        }
    }
}
