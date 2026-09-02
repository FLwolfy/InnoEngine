using System;

using Inno.Core.Mathematics;

namespace Inno.Editor.Rendering;

/// <summary>
/// Identifies the projection family represented by neutral Editor viewport navigation state.
/// </summary>
public enum EditorViewportProjection
{
    /// <summary>
    /// Uses a parallel projection controlled by an orthographic half-height.
    /// </summary>
    Orthographic,

    /// <summary>
    /// Uses a perspective projection controlled by a vertical field of view.
    /// </summary>
    Perspective
}

/// <summary>
/// Identifies the active interaction model without prescribing a rendering camera type.
/// </summary>
public enum EditorViewportNavigationMode
{
    /// <summary>
    /// Pans and zooms over a provider-defined plane.
    /// </summary>
    Planar,

    /// <summary>
    /// Rotates around a focus pivot and dollies along the view direction.
    /// </summary>
    Orbit,

    /// <summary>
    /// Moves and looks freely from the current view position.
    /// </summary>
    Fly
}

/// <summary>
/// Declares navigation operations supported by one viewport provider.
/// </summary>
[Flags]
public enum EditorViewportNavigationCapabilities
{
    /// <summary>
    /// Disables host-owned navigation.
    /// </summary>
    None = 0,

    /// <summary>
    /// Allows translating the view parallel to its image plane.
    /// </summary>
    Pan = 1 << 0,

    /// <summary>
    /// Allows changing orthographic size or perspective focus distance.
    /// </summary>
    Zoom = 1 << 1,

    /// <summary>
    /// Allows the complete plane-oriented pan and zoom interaction model.
    /// </summary>
    Planar = Pan | Zoom,

    /// <summary>
    /// Allows pivot-oriented orbit and dolly.
    /// </summary>
    Orbit = 1 << 2,

    /// <summary>
    /// Allows free-look movement.
    /// </summary>
    Fly = 1 << 3,

    /// <summary>
    /// Allows framing a provider-supplied or host-derived selection bound.
    /// </summary>
    FrameSelection = 1 << 4
}

/// <summary>
/// Identifies a provider-defined navigation profile across reload generations.
/// </summary>
public readonly record struct EditorViewportNavigationProfileId
{
    /// <summary>
    /// Creates a stable profile identifier.
    /// </summary>
    /// <param name="value">
    /// Globally stable profile identity.
    /// </param>
    public EditorViewportNavigationProfileId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        this.value = value.Trim();
    }

    /// <summary>
    /// Gets the stable profile identity.
    /// </summary>
    public string value { get; }

    /// <summary>
    /// Gets whether this identifier contains a usable value.
    /// </summary>
    public bool isValid => !string.IsNullOrWhiteSpace(value);

    /// <summary>
    /// Formats this value as a human-readable representation.
    /// </summary>
    /// <returns>
    /// The human-readable representation of this value.
    /// </returns>
    public override string ToString() => value ?? string.Empty;
}

/// <summary>
/// Describes a world-space sphere that can be framed by host navigation.
/// </summary>
public readonly record struct EditorViewportFocusBounds
{
    /// <summary>
    /// Creates a finite focus bound.
    /// </summary>
    /// <param name="center">
    /// World-space focus center.
    /// </param>
    /// <param name="radius">
    /// Non-negative world-space radius.
    /// </param>
    /// <exception cref="ArgumentException">
    /// Thrown when the center contains a non-finite value.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when the radius is negative or non-finite.
    /// </exception>
    public EditorViewportFocusBounds(Vector3 center, float radius)
    {
        if (!IsFinite(center))
            throw new ArgumentException("Focus center components must be finite.", nameof(center));
        if (!float.IsFinite(radius) || radius < 0f)
            throw new ArgumentOutOfRangeException(nameof(radius));
        this.center = center;
        this.radius = radius;
    }

    /// <summary>
    /// Gets the world-space focus center.
    /// </summary>
    public Vector3 center { get; }

    /// <summary>
    /// Gets the non-negative world-space radius.
    /// </summary>
    public float radius { get; }

    private static bool IsFinite(Vector3 value)
        => float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z);
}

/// <summary>
/// Describes how the host may navigate one provider-owned viewport without exposing the provider's camera model.
/// </summary>
public sealed class EditorViewportNavigationProfile
{
    /// <summary>
    /// Creates a provider-defined navigation profile.
    /// </summary>
    /// <param name="id">
    /// Stable profile identity.
    /// </param>
    /// <param name="capabilities">
    /// Operations accepted by the provider.
    /// </param>
    /// <param name="defaultMode">
    /// Preferred mode when current state is unsupported.
    /// </param>
    public EditorViewportNavigationProfile(
        EditorViewportNavigationProfileId id,
        EditorViewportNavigationCapabilities capabilities,
        EditorViewportNavigationMode defaultMode)
    {
        if (!id.isValid)
            throw new ArgumentException("A valid navigation profile ID is required.", nameof(id));
        const EditorViewportNavigationCapabilities validCapabilities
            = EditorViewportNavigationCapabilities.Pan
            | EditorViewportNavigationCapabilities.Zoom
            | EditorViewportNavigationCapabilities.Orbit
            | EditorViewportNavigationCapabilities.Fly
            | EditorViewportNavigationCapabilities.FrameSelection;
        if ((capabilities & ~validCapabilities) != 0)
            throw new ArgumentOutOfRangeException(nameof(capabilities));
        if (capabilities != EditorViewportNavigationCapabilities.None
            && !SupportsMode(capabilities, defaultMode))
        {
            throw new ArgumentException(
                "The default navigation mode must be enabled by the profile capabilities.",
                nameof(defaultMode));
        }
        this.id = id;
        this.capabilities = capabilities;
        this.defaultMode = defaultMode;
    }

    /// <summary>
    /// Gets the stable profile identity.
    /// </summary>
    public EditorViewportNavigationProfileId id { get; }

    /// <summary>
    /// Gets the operations accepted by this provider.
    /// </summary>
    public EditorViewportNavigationCapabilities capabilities { get; }

    /// <summary>
    /// Gets the mode selected when current state is not supported.
    /// </summary>
    public EditorViewportNavigationMode defaultMode { get; }

    /// <summary>
    /// Gets or sets the provider-defined world-up direction.
    /// </summary>
    public Vector3 worldUp { get; set; } = Vector3.UP;

    /// <summary>
    /// Gets or sets the optional current selection bound.
    /// </summary>
    public EditorViewportFocusBounds? focusBounds { get; set; }

    /// <summary>
    /// Gets or sets pointer rotation sensitivity in radians per pixel.
    /// </summary>
    public float rotationSensitivity { get; set; } = 0.005f;

    /// <summary>
    /// Gets or sets exponential wheel zoom sensitivity.
    /// </summary>
    public float zoomSensitivity { get; set; } = 0.16f;

    /// <summary>
    /// Gets or sets minimum orthographic half-height.
    /// </summary>
    public float minimumOrthographicSize { get; set; } = 0.001f;

    /// <summary>
    /// Gets or sets maximum orthographic half-height.
    /// </summary>
    public float maximumOrthographicSize { get; set; } = 100000f;

    /// <summary>
    /// Gets or sets minimum orbit or framing distance.
    /// </summary>
    public float minimumFocusDistance { get; set; } = 0.01f;

    /// <summary>
    /// Gets or sets maximum orbit or framing distance.
    /// </summary>
    public float maximumFocusDistance { get; set; } = 1000000f;

    /// <summary>
    /// Gets or sets the multiplier used while fast fly movement is requested.
    /// </summary>
    public float fastMovementMultiplier { get; set; } = 4f;

    /// <summary>
    /// Gets or sets additional framing space around selection bounds.
    /// </summary>
    public float framePadding { get; set; } = 1.25f;

    /// <summary>
    /// Gets a disabled fallback profile used when no provider navigation contract exists.
    /// </summary>
    public static EditorViewportNavigationProfile disabled { get; } = new(
        new EditorViewportNavigationProfileId("inno.editor.navigation.disabled"),
        EditorViewportNavigationCapabilities.None,
        EditorViewportNavigationMode.Planar);

    private static bool SupportsMode(
        EditorViewportNavigationCapabilities capabilities,
        EditorViewportNavigationMode mode)
        => mode switch
        {
            EditorViewportNavigationMode.Planar =>
                (capabilities & EditorViewportNavigationCapabilities.Planar) != 0,
            EditorViewportNavigationMode.Orbit =>
                capabilities.HasFlag(EditorViewportNavigationCapabilities.Orbit),
            EditorViewportNavigationMode.Fly =>
                capabilities.HasFlag(EditorViewportNavigationCapabilities.Fly),
            _ => false
        };
}

/// <summary>
/// Stores rendering-model-neutral navigation state for one Editor viewport across provider reloads.
/// </summary>
public sealed class EditorViewportNavigationState
{
    private const float C_MINIMUM_ORTHOGRAPHIC_SIZE = 0.001f;
    private const float C_MINIMUM_FIELD_OF_VIEW = 1f;
    private const float C_MAXIMUM_FIELD_OF_VIEW = 179f;

    private Vector3 m_position;
    private Quaternion m_rotation = Quaternion.identity;
    private Vector3 m_pivot;
    private EditorViewportProjection m_projection = EditorViewportProjection.Orthographic;
    private EditorViewportNavigationMode m_mode = EditorViewportNavigationMode.Planar;
    private float m_orthographicSize = 5f;
    private float m_fieldOfView = 60f;
    private float m_nearClip = 0.01f;
    private float m_farClip = 1000f;
    private float m_focusDistance = 10f;
    private float m_movementSpeed = 5f;

    /// <summary>
    /// Gets whether a provider or restored panel state initialized this state.
    /// </summary>
    public bool isInitialized { get; private set; }

    /// <summary>
    /// Gets or sets the current projection family.
    /// </summary>
    public EditorViewportProjection projection
    {
        get => m_projection;
        set
        {
            if (!Enum.IsDefined(value))
                throw new ArgumentOutOfRangeException(nameof(value));
            m_projection = value;
            isInitialized = true;
        }
    }

    /// <summary>
    /// Gets or sets the active host navigation mode.
    /// </summary>
    public EditorViewportNavigationMode mode
    {
        get => m_mode;
        set
        {
            if (!Enum.IsDefined(value))
                throw new ArgumentOutOfRangeException(nameof(value));
            m_mode = value;
            isInitialized = true;
        }
    }

    /// <summary>
    /// Gets or sets the provider-defined world-space view position.
    /// </summary>
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

    /// <summary>
    /// Gets or sets the normalized provider-defined world-space view rotation.
    /// </summary>
    public Quaternion rotation
    {
        get => m_rotation;
        set
        {
            ValidateFinite(value, nameof(value));
            if (value.LengthSquared() < 0.000001f)
                throw new ArgumentException("Navigation rotation must have a usable magnitude.", nameof(value));
            m_rotation = value.normalized;
            isInitialized = true;
        }
    }

    /// <summary>
    /// Gets or sets the world-space orbit and framing pivot.
    /// </summary>
    public Vector3 pivot
    {
        get => m_pivot;
        set
        {
            ValidateFinite(value, nameof(value));
            m_pivot = value;
            isInitialized = true;
        }
    }

    /// <summary>
    /// Gets or sets the positive orthographic half-height in provider world units.
    /// </summary>
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

    /// <summary>
    /// Gets or sets the perspective vertical field of view in degrees.
    /// </summary>
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

    /// <summary>
    /// Gets or sets the positive near clipping distance.
    /// </summary>
    public float nearClip
    {
        get => m_nearClip;
        set
        {
            if (!float.IsFinite(value) || value <= 0f || value >= m_farClip)
                throw new ArgumentOutOfRangeException(nameof(value));
            m_nearClip = value;
            isInitialized = true;
        }
    }

    /// <summary>
    /// Gets or sets the far clipping distance greater than the near distance.
    /// </summary>
    public float farClip
    {
        get => m_farClip;
        set
        {
            if (!float.IsFinite(value) || value <= m_nearClip)
                throw new ArgumentOutOfRangeException(nameof(value));
            m_farClip = value;
            isInitialized = true;
        }
    }

    /// <summary>
    /// Gets or sets the positive orbit and framing distance.
    /// </summary>
    public float focusDistance
    {
        get => m_focusDistance;
        set
        {
            if (!float.IsFinite(value) || value <= 0f)
                throw new ArgumentOutOfRangeException(nameof(value));
            m_focusDistance = value;
            isInitialized = true;
        }
    }

    /// <summary>
    /// Gets or sets the positive base movement speed in world units per second.
    /// </summary>
    public float movementSpeed
    {
        get => m_movementSpeed;
        set
        {
            if (!float.IsFinite(value) || value <= 0f)
                throw new ArgumentOutOfRangeException(nameof(value));
            m_movementSpeed = value;
        }
    }

    /// <summary>
    /// Atomically initializes an orthographic navigation view.
    /// </summary>
    /// <param name="position">
    /// Provider-defined world-space position.
    /// </param>
    /// <param name="rotation">
    /// Provider-defined world-space rotation.
    /// </param>
    /// <param name="orthographicSize">
    /// Positive orthographic half-height.
    /// </param>
    public void ConfigureOrthographic(Vector3 position, Quaternion rotation, float orthographicSize)
    {
        ValidateFinite(position, nameof(position));
        ValidateRotation(rotation, nameof(rotation));
        if (!float.IsFinite(orthographicSize)
            || orthographicSize < C_MINIMUM_ORTHOGRAPHIC_SIZE)
        {
            throw new ArgumentOutOfRangeException(nameof(orthographicSize));
        }
        m_position = position;
        m_rotation = rotation.normalized;
        m_orthographicSize = orthographicSize;
        m_projection = EditorViewportProjection.Orthographic;
        isInitialized = true;
    }

    /// <summary>
    /// Atomically initializes a perspective navigation view.
    /// </summary>
    /// <param name="position">
    /// Provider-defined world-space position.
    /// </param>
    /// <param name="rotation">
    /// Provider-defined world-space rotation.
    /// </param>
    /// <param name="fieldOfView">
    /// Perspective vertical field of view in degrees.
    /// </param>
    /// <param name="nearClip">
    /// Positive near clipping distance.
    /// </param>
    /// <param name="farClip">
    /// Far clipping distance greater than <paramref name="nearClip"/>.
    /// </param>
    public void ConfigurePerspective(
        Vector3 position,
        Quaternion rotation,
        float fieldOfView,
        float nearClip,
        float farClip)
    {
        ValidateFinite(position, nameof(position));
        ValidateRotation(rotation, nameof(rotation));
        if (!float.IsFinite(fieldOfView)
            || fieldOfView < C_MINIMUM_FIELD_OF_VIEW
            || fieldOfView > C_MAXIMUM_FIELD_OF_VIEW)
        {
            throw new ArgumentOutOfRangeException(nameof(fieldOfView));
        }
        if (!float.IsFinite(nearClip) || nearClip <= 0f)
            throw new ArgumentOutOfRangeException(nameof(nearClip));
        if (!float.IsFinite(farClip) || farClip <= nearClip)
            throw new ArgumentOutOfRangeException(nameof(farClip));
        m_position = position;
        m_rotation = rotation.normalized;
        m_fieldOfView = fieldOfView;
        m_nearClip = nearClip;
        m_farClip = farClip;
        m_projection = EditorViewportProjection.Perspective;
        isInitialized = true;
    }

    private static void ValidateFinite(Vector3 value, string parameterName)
    {
        if (!float.IsFinite(value.x) || !float.IsFinite(value.y) || !float.IsFinite(value.z))
            throw new ArgumentException("Navigation position components must be finite.", parameterName);
    }

    private static void ValidateFinite(Quaternion value, string parameterName)
    {
        if (!float.IsFinite(value.x)
            || !float.IsFinite(value.y)
            || !float.IsFinite(value.z)
            || !float.IsFinite(value.w))
        {
            throw new ArgumentException("Navigation rotation components must be finite.", parameterName);
        }
    }

    private static void ValidateRotation(Quaternion value, string parameterName)
    {
        ValidateFinite(value, parameterName);
        if (value.LengthSquared() < 0.000001f)
            throw new ArgumentException("Navigation rotation must have a usable magnitude.", parameterName);
    }
}
