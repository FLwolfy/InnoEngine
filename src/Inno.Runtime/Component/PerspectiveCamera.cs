using System;

using Inno.Core.ECS;
using Inno.Core.Logging;
using Inno.Core.Math;
using Inno.Core.Serialization;

namespace Inno.Runtime.Component;

/// <summary>
/// Perspective (2.5D/3D) camera component.
///
/// Conventions:
/// - Left-handed: +Z is forward.
/// - NDC depth range: 0..1 (matches your orthographic depth convention).
/// - Camera scale is ignored (Unity-style).
/// </summary>
public sealed class PerspectiveCamera : Camera
{
    private const float C_MIN_NEAR = 0.01f;
    private const float C_MIN_FOV  = 1f;
    private const float C_MAX_FOV  = 179f;

    private float m_fovDegrees = 60f;
    
    public override void OnAttach()
    {
        base.OnAttach();
        
        near = 10f;
        far = 1000f;
    }

    public override void Update()
    {
        near = Math.Max(C_MIN_NEAR, near);
    }

    [SerializableProperty]
    public float fovDegrees
    {
        get => m_fovDegrees;
        set
        {
            float v = Math.Clamp(value, C_MIN_FOV, C_MAX_FOV);
            if (!MathHelper.AlmostEquals(m_fovDegrees, v))
            {
                m_fovDegrees = v;
                MarkDirty();
            }
        }
    }

    protected override void RebuildMatrix(out Matrix view, out Matrix projection, out Rect visibleRect)
    {
        Vector3 pos = transform.worldPosition;
        Quaternion rot = transform.worldRotation;

        projection  = CalculateProjectionMatrix();
        view        = CalculateViewMatrix(pos, rot);

        // Perspective camera doesn't have a single "2D view rect" by default (frustum is 3D).
        // Keep a safe default. If you need 2D rect for tools, compute it elsewhere with an explicit reference plane.
        visibleRect = default;
    }

    /// <summary>
    /// Traditional view matrix (Unity-style): ignore camera scale.
    /// view = T(-pos) * R(invRot)
    /// </summary>
    private static Matrix CalculateViewMatrix(Vector3 pos, Quaternion rot)
    {
        Quaternion invRot = Quaternion.Inverse(rot);

        Matrix r = Matrix.CreateFromQuaternion(invRot);
        Matrix t = Matrix.CreateTranslation(-pos.x, -pos.y, -pos.z);

        return t * r;
    }

    /// <summary>
    /// Left-handed perspective projection (+Z forward), NDC depth 0..1.
    ///
    /// This matches the common D3D/Vulkan depth convention (0..1) and your current ortho assumption.
    /// Matrix layout is assumed consistent with your Matrix constructor & CreateTranslation usage.
    /// </summary>
    private Matrix CalculateProjectionMatrix()
    {
        float aspect = aspectRatio;
        if (aspect <= 0.0001f || float.IsNaN(aspect) || float.IsInfinity(aspect))
            aspect = 16f / 9f;

        float fovRad = m_fovDegrees * (MathF.PI / 180f);

        return Matrix.CreatePerspectiveFieldOfView(
            fovRad,
            aspect,
            near,
            far
        );
    }
}
