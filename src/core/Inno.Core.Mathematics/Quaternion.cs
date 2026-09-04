using System;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;

namespace Inno.Core.Mathematics;

/// <summary>
/// Represents a rotation as a normalized four-component quaternion.
/// </summary>
[DataContract]
public struct Quaternion : IEquatable<Quaternion>
{
    /// <summary>
    /// The horizontal or first component.
    /// </summary>
    [DataMember] public float x;
    /// <summary>
    /// The vertical or second component.
    /// </summary>
    [DataMember] public float y;
    /// <summary>
    /// The depth or third component.
    /// </summary>
    [DataMember] public float z;
    /// <summary>
    /// The homogeneous or fourth component.
    /// </summary>
    [DataMember] public float w;

    /// <summary>
    /// Creates a validated quaternion instance.
    /// </summary>
    /// <param name="x">
    /// The horizontal or first component.
    /// </param>
    /// <param name="y">
    /// The vertical or second component.
    /// </param>
    /// <param name="z">
    /// The depth or third component.
    /// </param>
    /// <param name="w">
    /// The homogeneous or fourth component.
    /// </param>
    public Quaternion(float x, float y, float z, float w)
    {
        this.x = x;
        this.y = y;
        this.z = z;
        this.w = w;
    }

    /// <summary>
    /// Gets the stable identity used to reference this value across subsystem boundaries.
    /// </summary>
    public static Quaternion identity => new Quaternion(0, 0, 0, 1);

    /// <summary>
    /// Calculates the Euclidean magnitude of this value.
    /// </summary>
    /// <returns>
    /// The scalar result calculated from the supplied inputs.
    /// </returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public float Length() => MathF.Sqrt(LengthSquared());

    /// <summary>
    /// Calculates the squared Euclidean magnitude without a square-root operation.
    /// </summary>
    /// <returns>
    /// The scalar result calculated from the supplied inputs.
    /// </returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public float LengthSquared() => SimdMath.Dot4(x, y, z, w, x, y, z, w);

    /// <summary>
    /// Gets a unit-length copy, or the zero value when normalization is undefined.
    /// </summary>
    public Quaternion normalized => Normalize(this);

    /// <summary>
    /// Returns a unit-length value while handling degenerate input according to the method contract.
    /// </summary>
    /// <param name="q">
    /// The q consumed by normalize; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <returns>
    /// The validated quaternion that represents the completed operation.
    /// </returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Quaternion Normalize(Quaternion q)
    {
        float len = q.Length();
        if (len < 1e-6f) return identity;
        return new Quaternion(q.x / len, q.y / len, q.z / len, q.w / len);
    }

    /// <summary>
    /// Returns the quaternion conjugate by negating its vector components.
    /// </summary>
    /// <param name="q">
    /// The q consumed by conjugate; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <returns>
    /// The validated quaternion that represents the completed operation.
    /// </returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Quaternion Conjugate(Quaternion q)
        => new Quaternion(-q.x, -q.y, -q.z, q.w);

    /// <summary>
    /// Calculates the inverse rotation represented by the supplied quaternion.
    /// </summary>
    /// <param name="q">
    /// The q consumed by inverse; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <returns>
    /// The validated quaternion that represents the completed operation.
    /// </returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Quaternion Inverse(Quaternion q)
    {
        float lenSq = q.LengthSquared();
        if (lenSq < 1e-6f) return identity;
        var conj = Conjugate(q);
        return new Quaternion(conj.x / lenSq, conj.y / lenSq, conj.z / lenSq, conj.w / lenSq);
    }

    /// <summary>
    /// Interpolates along the shortest spherical path between two rotations.
    /// </summary>
    /// <param name="a">
    /// The first operand or interpolation endpoint.
    /// </param>
    /// <param name="b">
    /// The second operand or interpolation endpoint.
    /// </param>
    /// <param name="t">
    /// The interpolation factor, where zero selects the first endpoint and one selects the second.
    /// </param>
    /// <returns>
    /// The validated quaternion that represents the completed operation.
    /// </returns>
    public static Quaternion Slerp(Quaternion a, Quaternion b, float t)
    {
        float dot = a.x * b.x + a.y * b.y + a.z * b.z + a.w * b.w;

        if (dot < 0f)
        {
            b = new Quaternion(-b.x, -b.y, -b.z, -b.w);
            dot = -dot;
        }

        if (dot > 0.9995f)
        {
            // Linear interpolation fallback
            Quaternion result = new Quaternion(
                a.x + t * (b.x - a.x),
                a.y + t * (b.y - a.y),
                a.z + t * (b.z - a.z),
                a.w + t * (b.w - a.w)
            );
            return Normalize(result);
        }

        float theta0 = MathF.Acos(dot);
        float sinTheta0 = MathF.Sin(theta0);
        float theta = theta0 * t;
        float sinTheta = MathF.Sin(theta);

        float s0 = MathF.Cos(theta) - dot * sinTheta / sinTheta0;
        float s1 = sinTheta / sinTheta0;

        return new Quaternion(
            a.x * s0 + b.x * s1,
            a.y * s0 + b.y * s1,
            a.z * s0 + b.z * s1,
            a.w * s0 + b.w * s1
        );
    }

    /// <summary>
    /// Creates and validates a caller-owned from axis angle value.
    /// </summary>
    /// <param name="axis">
    /// The axis consumed by create from axis angle; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <param name="angle">
    /// The angle consumed by create from axis angle; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <returns>
    /// The validated quaternion that represents the completed operation.
    /// </returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Quaternion CreateFromAxisAngle(Vector3 axis, float angle)
    {
        axis = axis.normalized;
        float halfAngle = angle * 0.5f;
        float sin = MathF.Sin(halfAngle);
        return new Quaternion(
            axis.x * sin,
            axis.y * sin,
            axis.z * sin,
            MathF.Cos(halfAngle)
        );
    }

    /// <summary>
    /// Creates a quaternion from a rotation matrix.
    /// </summary>
    /// <param name="m">
    /// The transformation matrix applied to the supplied value.
    /// </param>
    /// <returns>
    /// The validated quaternion that represents the completed operation.
    /// </returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Quaternion FromRotationMatrix(Matrix m)
    {
        float trace = m.m11 + m.m22 + m.m33;
        if (trace > 0f)
        {
            float s = MathF.Sqrt(trace + 1f) * 2f;
            float invS = 1f / s;
            return Normalize(new Quaternion(
                (m.m32 - m.m23) * invS,
                (m.m13 - m.m31) * invS,
                (m.m21 - m.m12) * invS,
                0.25f * s));
        }

        if (m.m11 > m.m22 && m.m11 > m.m33)
        {
            float s = MathF.Sqrt(1f + m.m11 - m.m22 - m.m33) * 2f;
            float invS = 1f / s;
            return Normalize(new Quaternion(
                0.25f * s,
                (m.m12 + m.m21) * invS,
                (m.m13 + m.m31) * invS,
                (m.m32 - m.m23) * invS));
        }

        if (m.m22 > m.m33)
        {
            float s = MathF.Sqrt(1f + m.m22 - m.m11 - m.m33) * 2f;
            float invS = 1f / s;
            return Normalize(new Quaternion(
                (m.m12 + m.m21) * invS,
                0.25f * s,
                (m.m23 + m.m32) * invS,
                (m.m13 - m.m31) * invS));
        }

        float sFinal = MathF.Sqrt(1f + m.m33 - m.m11 - m.m22) * 2f;
        float invFinal = 1f / sFinal;
        return Normalize(new Quaternion(
            (m.m13 + m.m31) * invFinal,
            (m.m23 + m.m32) * invFinal,
            0.25f * sFinal,
            (m.m21 - m.m12) * invFinal));
    }

    /// <summary>
    /// Creates a rotation quaternion that looks in <paramref name="forward"/> direction with the given <paramref name="up"/>.
    /// </summary>
    /// <param name="forward">
    /// The forward consumed by look rotation; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <param name="up">
    /// The up consumed by look rotation; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <returns>
    /// The validated quaternion that represents the completed operation.
    /// </returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Quaternion LookRotation(Vector3 forward, Vector3 up)
    {
        Vector3 z = Vector3.NormalizeSafe(forward);
        Vector3 x = Vector3.NormalizeSafe(Vector3.Cross(up, z));
        Vector3 y = Vector3.Cross(z, x);

        var m = new Matrix(
            x.x, y.x, z.x, 0f,
            x.y, y.y, z.y, 0f,
            x.z, y.z, z.z, 0f,
            0f,  0f,  0f,  1f);

        return FromRotationMatrix(m);
    }

    /// <summary>
    /// Converts this quaternion to a rotation matrix.
    /// </summary>
    /// <returns>
    /// The validated matrix that represents the completed operation.
    /// </returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Matrix ToMatrix() => Matrix.CreateFromQuaternion(this);

    /// <summary>
    /// Creates and validates a caller-owned from yaw pitch roll value.
    /// </summary>
    /// <param name="yaw">
    /// The yaw consumed by create from yaw pitch roll; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <param name="pitch">
    /// The pitch consumed by create from yaw pitch roll; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <param name="roll">
    /// The roll consumed by create from yaw pitch roll; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <returns>
    /// The validated quaternion that represents the completed operation.
    /// </returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Quaternion CreateFromYawPitchRoll(float yaw, float pitch, float roll)
        => FromEulerAnglesZYX(new Vector3(pitch, yaw, roll));

    /// <summary>
    /// Converts this value to its euler angles xyz representation.
    /// </summary>
    /// <returns>
    /// The validated vector3 that represents the completed operation.
    /// </returns>
    public Vector3 ToEulerAnglesXYZ()
    {
        Quaternion normalized = this.normalized;
        float sinY = Math.Clamp(
            2f * (normalized.x * normalized.z + normalized.w * normalized.y),
            -1f,
            1f);
        float angleY = MathF.Asin(sinY);
        float angleX = MathF.Atan2(
            2f * (normalized.w * normalized.x - normalized.y * normalized.z),
            1f - 2f * (normalized.x * normalized.x + normalized.y * normalized.y));
        float angleZ = MathF.Atan2(
            2f * (normalized.w * normalized.z - normalized.x * normalized.y),
            1f - 2f * (normalized.y * normalized.y + normalized.z * normalized.z));
        return new Vector3(angleX, angleY, angleZ);
    }
    
    /// <summary>
    /// Converts this value to its euler angles xyzdegrees representation.
    /// </summary>
    /// <returns>
    /// The validated vector3 that represents the completed operation.
    /// </returns>
    public Vector3 ToEulerAnglesXYZDegrees()
        => ToEulerAnglesXYZ() * (180f / MathF.PI);

    /// <summary>
    /// Converts this value to its euler angles zyx representation.
    /// </summary>
    /// <returns>
    /// The validated vector3 that represents the completed operation.
    /// </returns>
    public Vector3 ToEulerAnglesZYX()
    {
        float sinrCosp = 2 * (w * z + x * y);
        float cosrCosp = 1 - 2 * (y * y + z * z);
        float angleZ = MathF.Atan2(sinrCosp, cosrCosp);

        float sinp = 2 * (w * y - z * x);
        float angleY = MathF.Abs(sinp) >= 1
            ? MathF.CopySign(MathF.PI / 2, sinp)
            : MathF.Asin(sinp);

        float sinyCosp = 2 * (w * x + y * z);
        float cosyCosp = 1 - 2 * (x * x + y * y);
        float angleX = MathF.Atan2(sinyCosp, cosyCosp);

        return new Vector3(angleX, angleY, angleZ);
    }
    
    /// <summary>
    /// Converts this value to its euler angles zyxdegrees representation.
    /// </summary>
    /// <returns>
    /// The validated vector3 that represents the completed operation.
    /// </returns>
    public Vector3 ToEulerAnglesZYXDegrees()
    {
        float sinrCosp = 2 * (w * z + x * y);
        float cosrCosp = 1 - 2 * (y * y + z * z);
        float angleZ = MathF.Atan2(sinrCosp, cosrCosp);

        float sinp = 2 * (w * y - z * x);
        float angleY = MathF.Abs(sinp) >= 1
            ? MathF.CopySign(MathF.PI / 2, sinp)
            : MathF.Asin(sinp);

        float sinyCosp = 2 * (w * x + y * z);
        float cosyCosp = 1 - 2 * (x * x + y * y);
        float angleX = MathF.Atan2(sinyCosp, cosyCosp);

        return new Vector3(
            angleX * 180f / MathF.PI,
            angleY * 180f / MathF.PI,
            angleZ * 180f / MathF.PI
        );
    }

    /// <summary>
    /// Creates the target representation from the supplied euler angles xyz value.
    /// </summary>
    /// <param name="euler">
    /// The euler consumed by from euler angles xyz; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <returns>
    /// The validated quaternion that represents the completed operation.
    /// </returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Quaternion FromEulerAnglesXYZ(Vector3 euler)
    {
        float cx = MathF.Cos(euler.x * 0.5f);
        float sx = MathF.Sin(euler.x * 0.5f);
        float cy = MathF.Cos(euler.y * 0.5f);
        float sy = MathF.Sin(euler.y * 0.5f);
        float cz = MathF.Cos(euler.z * 0.5f);
        float sz = MathF.Sin(euler.z * 0.5f);

        return new Quaternion(
            sx * cy * cz + cx * sy * sz,
            cx * sy * cz - sx * cy * sz,
            cx * cy * sz + sx * sy * cz,
            cx * cy * cz - sx * sy * sz
        );
    }
    
    /// <summary>
    /// Creates the target representation from the supplied euler angles xyzdegrees value.
    /// </summary>
    /// <param name="eulerDegrees">
    /// The euler degrees consumed by from euler angles xyzdegrees; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <returns>
    /// The validated quaternion that represents the completed operation.
    /// </returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Quaternion FromEulerAnglesXYZDegrees(Vector3 eulerDegrees)
    {
        var euler = eulerDegrees * (MathF.PI / 180f);

        float cx = MathF.Cos(euler.x * 0.5f);
        float sx = MathF.Sin(euler.x * 0.5f);
        float cy = MathF.Cos(euler.y * 0.5f);
        float sy = MathF.Sin(euler.y * 0.5f);
        float cz = MathF.Cos(euler.z * 0.5f);
        float sz = MathF.Sin(euler.z * 0.5f);

        return new Quaternion(
            sx * cy * cz + cx * sy * sz,
            cx * sy * cz - sx * cy * sz,
            cx * cy * sz + sx * sy * cz,
            cx * cy * cz - sx * sy * sz
        );
    }

    /// <summary>
    /// Creates the target representation from the supplied euler angles zyx value.
    /// </summary>
    /// <param name="euler">
    /// The euler consumed by from euler angles zyx; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <returns>
    /// The validated quaternion that represents the completed operation.
    /// </returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Quaternion FromEulerAnglesZYX(Vector3 euler)
    {
        float cz = MathF.Cos(euler.z * 0.5f);
        float sz = MathF.Sin(euler.z * 0.5f);
        float cy = MathF.Cos(euler.y * 0.5f);
        float sy = MathF.Sin(euler.y * 0.5f);
        float cx = MathF.Cos(euler.x * 0.5f);
        float sx = MathF.Sin(euler.x * 0.5f);

        return new Quaternion(
            sx * cy * cz - cx * sy * sz,
            cx * sy * cz + sx * cy * sz,
            cx * cy * sz - sx * sy * cz,
            cx * cy * cz + sx * sy * sz
        );
    }
    
    /// <summary>
    /// Creates the target representation from the supplied euler angles zyxdegrees value.
    /// </summary>
    /// <param name="eulerDegrees">
    /// The euler degrees consumed by from euler angles zyxdegrees; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <returns>
    /// The validated quaternion that represents the completed operation.
    /// </returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Quaternion FromEulerAnglesZYXDegrees(Vector3 eulerDegrees)
    {
        var euler = eulerDegrees * (MathF.PI / 180f);

        float cz = MathF.Cos(euler.z * 0.5f);
        float sz = MathF.Sin(euler.z * 0.5f);
        float cy = MathF.Cos(euler.y * 0.5f);
        float sy = MathF.Sin(euler.y * 0.5f);
        float cx = MathF.Cos(euler.x * 0.5f);
        float sx = MathF.Sin(euler.x * 0.5f);

        return new Quaternion(
            sx * cy * cz - cx * sy * sz,
            cx * sy * cz + sx * cy * sz,
            cx * cy * sz - sx * sy * cz,
            cx * cy * cz + sx * sy * sz
        );
    }

    /// <summary>
    /// Multiplies the supplied values according to their algebraic contract.
    /// </summary>
    /// <param name="a">
    /// The first operand or interpolation endpoint.
    /// </param>
    /// <param name="b">
    /// The second operand or interpolation endpoint.
    /// </param>
    /// <returns>
    /// The validated quaternion that represents the completed operation.
    /// </returns>
    public static Quaternion operator *(Quaternion a, Quaternion b)
    {
        return new Quaternion(
            a.w * b.x + a.x * b.w + a.y * b.z - a.z * b.y,
            a.w * b.y - a.x * b.z + a.y * b.w + a.z * b.x,
            a.w * b.z + a.x * b.y - a.y * b.x + a.z * b.w,
            a.w * b.w - a.x * b.x - a.y * b.y - a.z * b.z
        );
    }

    /// <summary>
    /// Determines whether the supplied values are equal under the type's equality tolerance.
    /// </summary>
    /// <param name="a">
    /// The first operand or interpolation endpoint.
    /// </param>
    /// <param name="b">
    /// The second operand or interpolation endpoint.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the requested condition is satisfied; otherwise, <see langword="false"/>.
    /// </returns>
    public static bool operator ==(Quaternion a, Quaternion b)
        => MathHelper.AlmostEquals(a.x, b.x) &&
           MathHelper.AlmostEquals(a.y, b.y) &&
           MathHelper.AlmostEquals(a.z, b.z) &&
           MathHelper.AlmostEquals(a.w, b.w);

    /// <summary>
    /// Determines whether the supplied values differ under the type's equality tolerance.
    /// </summary>
    /// <param name="a">
    /// The first operand or interpolation endpoint.
    /// </param>
    /// <param name="b">
    /// The second operand or interpolation endpoint.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the requested condition is satisfied; otherwise, <see langword="false"/>.
    /// </returns>
    public static bool operator !=(Quaternion a, Quaternion b)
        => !(a == b);
    
    /// <summary>
    /// Converts the supplied value to <see cref="System.Numerics.Quaternion"/>.
    /// </summary>
    /// <param name="q">
    /// The q consumed by convert; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <returns>
    /// The validated system.numerics.quaternion that represents the completed operation.
    /// </returns>
    public static implicit operator System.Numerics.Quaternion(Quaternion q) => new(q.x, q.y, q.z, q.w);
    /// <summary>
    /// Converts the supplied value to <see cref="Quaternion"/>.
    /// </summary>
    /// <param name="q">
    /// The q consumed by convert; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <returns>
    /// The validated quaternion that represents the completed operation.
    /// </returns>
    public static implicit operator Quaternion(System.Numerics.Quaternion q) => new(q.X, q.Y, q.Z, q.W);

    /// <summary>
    /// Determines whether this value and the supplied value represent the same logical state.
    /// </summary>
    /// <param name="obj">
    /// The object compared with this value.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the requested condition is satisfied; otherwise, <see langword="false"/>.
    /// </returns>
    public override bool Equals(object? obj) => obj is Quaternion q && this == q;

    /// <summary>
    /// Determines whether this value and the supplied value represent the same logical state.
    /// </summary>
    /// <param name="other">
    /// The strongly typed value compared with this value.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the requested condition is satisfied; otherwise, <see langword="false"/>.
    /// </returns>
    public bool Equals(Quaternion other) => this == other;

    /// <summary>
    /// Computes a hash code consistent with the implemented equality contract.
    /// </summary>
    /// <returns>
    /// The scalar result calculated from the supplied inputs.
    /// </returns>
    public override int GetHashCode()
        => HashCode.Combine(x, y, z, w);

    /// <summary>
    /// Formats this value as a human-readable component list.
    /// </summary>
    /// <returns>
    /// The validated text representation owned by the caller.
    /// </returns>
    public override string ToString()
        => $"({x:F3}, {y:F3}, {z:F3}, {w:F3})";
}
