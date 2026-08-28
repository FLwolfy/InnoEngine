using System;
using System.Collections.Generic;
using Inno.Core.Mathematics;

namespace Inno.Rendering;

/// <summary>
/// Provides backend-neutral object-ID encoding and bounds picking shared by runtime and editor tools.
/// </summary>
public static class RenderPicking
{
    /// <summary>
    /// Encodes the GPU-visible portion of a persistent renderer identity into normalized color channels.
    /// </summary>
    /// <param name="persistentId">Persistent renderer identity, or an empty identity for no selection.</param>
    /// <returns>A normalized RGBA object token matching the built-in Picking pass.</returns>
    public static Vector4 EncodeObjectId(Guid persistentId)
    {
        Span<byte> bytes = stackalloc byte[16];
        persistentId.TryWriteBytes(bytes);
        return new Vector4(
            bytes[0] / 255f,
            bytes[1] / 255f,
            bytes[2] / 255f,
            bytes[3] / 255f);
    }

    /// <summary>
    /// Selects the nearest renderer whose conservative world bounds intersect a view-space pointer ray.
    /// </summary>
    /// <param name="objects">Candidate render objects, typically from one immutable world snapshot.</param>
    /// <param name="view">View used to render the pointer target.</param>
    /// <param name="normalizedX">Horizontal pointer coordinate from zero at left to one at right.</param>
    /// <param name="normalizedY">Vertical pointer coordinate from zero at top to one at bottom.</param>
    /// <param name="rendererId">Nearest renderer identity when an intersection is found.</param>
    /// <returns><see langword="true"/> when at least one visible renderer bound intersects the ray.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when a normalized pointer coordinate is non-finite or outside zero through one.
    /// </exception>
    public static bool TryPickBounds(
        IReadOnlyList<RenderObjectData> objects,
        RenderView view,
        float normalizedX,
        float normalizedY,
        out Guid rendererId)
    {
        ArgumentNullException.ThrowIfNull(objects);
        ArgumentNullException.ThrowIfNull(view);
        ValidateNormalized(normalizedX, nameof(normalizedX));
        ValidateNormalized(normalizedY, nameof(normalizedY));

        Matrix inverseViewProjection = Matrix.Invert(view.projectionMatrix * view.viewMatrix);
        Vector3 near = Unproject(inverseViewProjection, normalizedX, normalizedY, 0f);
        Vector3 far = Unproject(inverseViewProjection, normalizedX, normalizedY, 1f);
        Vector3 direction = (far - near).normalized;
        float nearestDistance = float.PositiveInfinity;
        rendererId = Guid.Empty;
        foreach (RenderObjectData renderObject in objects)
        {
            if (!view.cullingMask.Contains(renderObject.layer)
                || !TryIntersect(near, direction, renderObject.bounds, out float distance)
                || distance >= nearestDistance)
            {
                continue;
            }

            nearestDistance = distance;
            rendererId = renderObject.persistentId;
        }

        return rendererId != Guid.Empty;
    }

    private static Vector3 Unproject(
        Matrix inverseViewProjection,
        float normalizedX,
        float normalizedY,
        float depth)
    {
        Vector4 homogeneous = inverseViewProjection * new Vector4(
            normalizedX * 2f - 1f,
            1f - normalizedY * 2f,
            depth,
            1f);
        float reciprocalW = MathF.Abs(homogeneous.w) > MathHelper.C_TOLERANCE
            ? 1f / homogeneous.w
            : 1f;
        return new Vector3(
            homogeneous.x * reciprocalW,
            homogeneous.y * reciprocalW,
            homogeneous.z * reciprocalW);
    }

    private static bool TryIntersect(
        Vector3 origin,
        Vector3 direction,
        RenderBounds bounds,
        out float distance)
    {
        Vector3 minimum = bounds.center - bounds.extents;
        Vector3 maximum = bounds.center + bounds.extents;
        float near = 0f;
        float far = float.PositiveInfinity;
        if (!IntersectAxis(origin.x, direction.x, minimum.x, maximum.x, ref near, ref far)
            || !IntersectAxis(origin.y, direction.y, minimum.y, maximum.y, ref near, ref far)
            || !IntersectAxis(origin.z, direction.z, minimum.z, maximum.z, ref near, ref far))
        {
            distance = 0f;
            return false;
        }

        distance = near;
        return true;
    }

    private static bool IntersectAxis(
        float origin,
        float direction,
        float minimum,
        float maximum,
        ref float near,
        ref float far)
    {
        if (MathF.Abs(direction) <= MathHelper.C_TOLERANCE)
        {
            return origin >= minimum && origin <= maximum;
        }

        float reciprocal = 1f / direction;
        float first = (minimum - origin) * reciprocal;
        float second = (maximum - origin) * reciprocal;
        if (first > second)
        {
            (first, second) = (second, first);
        }

        near = MathF.Max(near, first);
        far = MathF.Min(far, second);
        return near <= far && far >= 0f;
    }

    private static void ValidateNormalized(float value, string parameterName)
    {
        if (!float.IsFinite(value) || value < 0f || value > 1f)
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }
    }
}
