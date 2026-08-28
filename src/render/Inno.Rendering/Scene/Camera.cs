using System;
using Inno.Core.Mathematics;
using Inno.Core.Reflection;
using Inno.Core.Serialization;
using Inno.Engine.Scene;
using Inno.Engine.Scene.Layers;

namespace Inno.Rendering;

/// <summary>
/// Defines a runtime camera while leaving device and pipeline ownership to Rendering.
/// </summary>
[StableTypeId("e9d48f90-0ae5-4f6a-9c26-bbc2cbbe4301")]
public sealed class Camera : GameComponent
{
    private float m_fieldOfView = 60f;
    private float m_orthographicSize = 5f;
    private float m_nearClipPlane = 0.1f;
    private float m_farClipPlane = 1000f;

    /// <summary>Gets or sets the projection model.</summary>
    [SerializableProperty]
    public CameraProjection projection { get; set; } = CameraProjection.Perspective;

    /// <summary>Gets or sets vertical perspective field of view in degrees.</summary>
    [SerializableProperty]
    public float fieldOfView
    {
        get => m_fieldOfView;
        set => m_fieldOfView = Math.Clamp(value, 1f, 179f);
    }

    /// <summary>Gets or sets half of the orthographic vertical extent.</summary>
    [SerializableProperty]
    public float orthographicSize
    {
        get => m_orthographicSize;
        set => m_orthographicSize = Math.Max(0.0001f, value);
    }

    /// <summary>Gets or sets the positive near clipping distance.</summary>
    [SerializableProperty]
    public float nearClipPlane
    {
        get => m_nearClipPlane;
        set => m_nearClipPlane = Math.Clamp(value, 0.0001f, m_farClipPlane - 0.0001f);
    }

    /// <summary>Gets or sets the far clipping distance.</summary>
    [SerializableProperty]
    public float farClipPlane
    {
        get => m_farClipPlane;
        set => m_farClipPlane = Math.Max(m_nearClipPlane + 0.0001f, value);
    }

    /// <summary>Gets or sets target initialization behavior.</summary>
    [SerializableProperty]
    public CameraClearMode clearMode { get; set; } = CameraClearMode.Sky;

    /// <summary>Gets or sets the linear fallback background color.</summary>
    [SerializableProperty]
    public Color backgroundColor { get; set; } = Color.BLACK;

    /// <summary>Gets or sets scene layers visible to this camera.</summary>
    [SerializableProperty]
    public GameLayerMask cullingMask { get; set; } = GameLayerMask.everything;

    /// <summary>Gets or sets a per-camera render-path override.</summary>
    [SerializableProperty]
    public RenderPath renderPath { get; set; } = RenderPath.Automatic;

    /// <summary>Gets or sets ascending camera scheduling priority.</summary>
    [SerializableProperty]
    public int priority { get; set; }

    /// <summary>Gets or sets an optional offscreen target.</summary>
    public RenderTexture? targetTexture { get; set; }

    /// <summary>
    /// Creates immutable request data from the current transform and camera settings.
    /// </summary>
    /// <param name="pixelWidth">Target width in pixels.</param>
    /// <param name="pixelHeight">Target height in pixels.</param>
    /// <returns>A left-handed view before backend-specific projection correction.</returns>
    public RenderRequest CreateRenderRequest(int pixelWidth, int pixelHeight)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pixelWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pixelHeight);
        Vector3 position = transform.worldPosition;
        Vector3 forward = Vector3.Transform(Vector3.FORWARD, transform.worldRotation);
        Vector3 up = Vector3.Transform(Vector3.UP, transform.worldRotation);
        Matrix viewMatrix = Matrix.CreateLookAt(position, position + forward, up);
        float aspect = (float)pixelWidth / pixelHeight;
        Matrix projectionMatrix = projection == CameraProjection.Perspective
            ? Matrix.CreatePerspectiveFieldOfView(
                MathHelper.ToRadians(m_fieldOfView),
                aspect,
                m_nearClipPlane,
                m_farClipPlane)
            : Matrix.CreateOrthographic(
                m_orthographicSize * 2f * aspect,
                m_orthographicSize * 2f,
                m_nearClipPlane,
                m_farClipPlane);
        RenderView view = new(
            viewMatrix,
            projectionMatrix,
            position,
            pixelWidth,
            pixelHeight,
            cullingMask);
        RenderTarget target = targetTexture is null
            ? RenderTarget.backbuffer
            : RenderTarget.FromTexture(targetTexture);
        return new RenderRequest(
            $"Camera:{gameObject.name}",
            view,
            target,
            renderPath,
            clearMode,
            backgroundColor,
            priority);
    }

    /// <inheritdoc />
    protected override void Reset()
    {
        projection = CameraProjection.Perspective;
        m_fieldOfView = 60f;
        m_orthographicSize = 5f;
        m_nearClipPlane = 0.1f;
        m_farClipPlane = 1000f;
        clearMode = CameraClearMode.Sky;
        backgroundColor = Color.BLACK;
        cullingMask = GameLayerMask.everything;
        renderPath = RenderPath.Automatic;
        priority = 0;
        targetTexture = null;
    }
}
