using System;

using Inno.Core.ECS;
using Inno.Core.Math;
using Inno.Core.Serialization;

namespace Inno.Runtime.Component;

/// <summary>
/// Orthographic camera component (3D transform: position/rotation),
/// with orthographic projection.
/// </summary>
public sealed class OrthographicCamera : Camera
{
    private float m_size = 1080f;

    public override void OnAttach()
    {
        base.OnAttach();
        
        near = -1000f;
        far = 1000f;
    }

    /// <summary>
    /// The size of the camera's view in world units.
    /// </summary>
    [SerializableProperty]
    public float size
    {
        get => m_size;
        set
        {
            if (!MathHelper.AlmostEquals(m_size, value))
            {
                m_size = value;
                MarkDirty();
            }
        }
    }

    protected override void RebuildMatrix(out Matrix view, out Matrix projection, out Rect visibleRect)
    {
        // Full 3D camera transform
        Vector3 pos = transform.worldPosition;
        Quaternion rot = transform.worldRotation;

        projection = CalculateProjectionMatrix();
        view = CalculateViewMatrix(pos, rot);
        visibleRect = CalculateViewRectFromInverseVP(projection, view);
    }

    private Matrix CalculateProjectionMatrix()
    {
        float halfHeight = m_size * 0.5f;
        float halfWidth  = halfHeight * aspectRatio;

        return Matrix.CreateOrthographic(
            width:  halfWidth * 2f,
            height: halfHeight * 2f,
            near,
            far
        );
    }

    /// <summary>
    /// Traditional correct view matrix:
    /// </summary>
    private Matrix CalculateViewMatrix(Vector3 pos, Quaternion rot)
    {
        Matrix r = Matrix.CreateFromQuaternion(rot);
        Matrix t = Matrix.CreateTranslation(pos.x, pos.y, pos.z);
        Matrix world = r * t;
        
        return Matrix.Invert(world);
    }

    /// <summary>
    /// Computes a screen-aligned XY AABB of the camera frustum slice by unprojecting the NDC quad
    /// with inverse(VP). Works with full 3D rotation + scale.
    /// </summary>
    private Rect CalculateViewRectFromInverseVP(Matrix projection, Matrix view)
    {
        Matrix vp = projection * view;
        Matrix invVP = Matrix.Invert(vp);

        // NDC corners (x,y) in [-1,1]. For orthographic, z can be any slice; choose near plane (0) or far (1)
        // depending on your clip space convention. We'll sample both and combine to be robust.
        // If your engine uses OpenGL-style NDC z [-1,1], adjust accordingly inside UnprojectNdc().
        Vector3[] ndc =
        [
            new(-1f, -1f, 0f),
            new( 1f, -1f, 0f),
            new( 1f,  1f, 0f),
            new(-1f,  1f, 0f),

            new(-1f, -1f, 1f),
            new( 1f, -1f, 1f),
            new( 1f,  1f, 1f),
            new(-1f,  1f, 1f),
        ];

        // Unproject all corners; take XY AABB
        float minX = float.PositiveInfinity;
        float maxX = float.NegativeInfinity;
        float minY = float.PositiveInfinity;
        float maxY = float.NegativeInfinity;

        for (int i = 0; i < ndc.Length; i++)
        {
            Vector3 w = UnprojectNdc(invVP, ndc[i]);

            if (w.x < minX) minX = w.x;
            if (w.x > maxX) maxX = w.x;
            if (w.y < minY) minY = w.y;
            if (w.y > maxY) maxY = w.y;
        }

        // Convert to your Rect (int-based). Use Floor/Ceil so you don't accidentally under-cull.
        int x = (int)MathF.Floor(minX);
        int y = (int)MathF.Floor(minY);
        int wInt = (int)MathF.Ceiling(maxX - minX);
        int hInt = (int)MathF.Ceiling(maxY - minY);

        return new Rect(x: x, y: y, width: wInt, height: hInt);
    }

    /// <summary>
    /// Unprojects an NDC position using inverse(VP).
    /// Assumes you have a Vector4 + Matrix multiply.
    /// If your clip space Z is [-1,1] instead of [0,1], adjust ndc.z mapping accordingly.
    /// </summary>
    private static Vector3 UnprojectNdc(Matrix invVP, Vector3 ndc)
    {
        Vector4 clip = new Vector4(ndc.x, ndc.y, ndc.z, 1f);
        Vector4 wh = invVP * clip;

        float invW = wh.w != 0f ? 1f / wh.w : 0f;
        return new Vector3(wh.x * invW, wh.y * invW, wh.z * invW);
    }
}
