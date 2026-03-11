using System;
using Inno.Core.Math;
using Inno.Core.Serialization;

namespace Inno.Core.ECS;

public abstract class Camera : GameComponent
{
    public sealed override ComponentTag orderTag => ComponentTag.Camera;

    private bool m_dirtyMatrix = true;

    private Matrix m_cachedViewMatrix;
    private Matrix m_cachedProjectionMatrix;
    private Rect m_cachedViewRect;
    
    private float m_aspectRatio = 16f / 9f;
    private float m_near = 0f;
    private float m_far  = 1000f;

    /// <summary>
    /// Sets or gets whether this camera is the main camera in the scene.
    /// </summary>
    [SerializableProperty]
    public bool isMainCamera
    {
        get => gameObject.scene.mainCamera == this;
        set
        {
            if ((gameObject.scene.mainCamera == this) != value)
            {
                gameObject.scene.mainCamera = value ? this : null;
            }
        }
    }

    /// <summary>
    /// The aspect ratio of the camera's view (width / height).
    /// Default is 16:9 (1.7777).
    /// This affects how the camera's view is rendered, especially in different screen resolutions.
    /// </summary>
    [SerializableProperty]
    public float aspectRatio
    {
        get => m_aspectRatio;
        set
        {
            if (MathF.Abs(m_aspectRatio - value) > 0.0001f)
            {
                m_aspectRatio = value;
                MarkDirty();
            }
        }
    }
    
    /// <summary>
    /// Near clipping plane.
    /// </summary>
    [SerializableProperty]
    public float near
    {
        get => m_near;
        set
        {
            if (!MathHelper.AlmostEquals(m_near, value))
            {
                m_near = value;
                MarkDirty();
            }
        }
    }

    /// <summary>
    /// Far clipping plane.
    /// </summary>
    [SerializableProperty]
    public float far
    {
        get => m_far;
        set
        {
            if (!MathHelper.AlmostEquals(m_far, value))
            {
                m_far = value;
                MarkDirty();
            }
        }
    }

    /// <summary>
    /// The view matrix of the camera.
    /// This matrix transforms world coordinates into camera space.
    /// </summary>
    public Matrix viewMatrix
    {
        get
        {
            EnsureMatrix();
            return m_cachedViewMatrix;
        }
    }

    /// <summary>
    /// The projection matrix of the camera.
    /// This matrix defines how the camera's view is projected onto the screen.
    /// </summary>
    public Matrix projectionMatrix
    {
        get
        {
            EnsureMatrix();
            return m_cachedProjectionMatrix;
        }
    }

    /// <summary>
    /// The view rectangle of the camera.
    /// This rectangle defines the area of the world that is visible through the camera.
    /// </summary>
    public Rect viewRect
    {
        get
        {
            EnsureMatrix();
            return m_cachedViewRect;
        }
    }

    protected void MarkDirty()
    {
        m_dirtyMatrix = true;
    }

    private void EnsureMatrix()
    {
        if (m_dirtyMatrix)
        {
            RebuildMatrix(out m_cachedViewMatrix, out m_cachedProjectionMatrix, out m_cachedViewRect);
            m_dirtyMatrix = false;
        }
    }

    protected abstract void RebuildMatrix(out Matrix view, out Matrix projection, out Rect visibleRect);

    public override void OnAttach()
    {
        transform.OnTransformChanged += MarkDirty;
    }

    public override void OnDetach()
    {
        if (isMainCamera)
        {
            isMainCamera = false;
        }

        transform.OnTransformChanged -= MarkDirty;
    }
}