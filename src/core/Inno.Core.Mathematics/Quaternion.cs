using System;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;

namespace Inno.Core.Mathematics;

[DataContract]
public struct Quaternion : IEquatable<Quaternion>
{
    [DataMember] public float x;
    [DataMember] public float y;
    [DataMember] public float z;
    [DataMember] public float w;

    public Quaternion(float x, float y, float z, float w)
    {
        this.x = x;
        this.y = y;
        this.z = z;
        this.w = w;
    }

    public static Quaternion identity => new Quaternion(0, 0, 0, 1);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public float Length() => MathF.Sqrt(LengthSquared());

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public float LengthSquared() => SimdMath.Dot4(x, y, z, w, x, y, z, w);

    public Quaternion normalized => Normalize(this);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Quaternion Normalize(Quaternion q)
    {
        float len = q.Length();
        if (len < 1e-6f) return identity;
        return new Quaternion(q.x / len, q.y / len, q.z / len, q.w / len);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Quaternion Conjugate(Quaternion q)
        => new Quaternion(-q.x, -q.y, -q.z, q.w);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Quaternion Inverse(Quaternion q)
    {
        float lenSq = q.LengthSquared();
        if (lenSq < 1e-6f) return identity;
        var conj = Conjugate(q);
        return new Quaternion(conj.x / lenSq, conj.y / lenSq, conj.z / lenSq, conj.w / lenSq);
    }

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
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Matrix ToMatrix() => Matrix.CreateFromQuaternion(this);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Quaternion CreateFromYawPitchRoll(float yaw, float pitch, float roll)
        => FromEulerAnglesZYX(new Vector3(pitch, yaw, roll));

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
    
    public Vector3 ToEulerAnglesXYZDegrees()
        => ToEulerAnglesXYZ() * (180f / MathF.PI);

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

    public static Quaternion operator *(Quaternion a, Quaternion b)
    {
        return new Quaternion(
            a.w * b.x + a.x * b.w + a.y * b.z - a.z * b.y,
            a.w * b.y - a.x * b.z + a.y * b.w + a.z * b.x,
            a.w * b.z + a.x * b.y - a.y * b.x + a.z * b.w,
            a.w * b.w - a.x * b.x - a.y * b.y - a.z * b.z
        );
    }

    public static bool operator ==(Quaternion a, Quaternion b)
        => MathHelper.AlmostEquals(a.x, b.x) &&
           MathHelper.AlmostEquals(a.y, b.y) &&
           MathHelper.AlmostEquals(a.z, b.z) &&
           MathHelper.AlmostEquals(a.w, b.w);

    public static bool operator !=(Quaternion a, Quaternion b)
        => !(a == b);
    
    public static implicit operator System.Numerics.Quaternion(Quaternion q) => new(q.x, q.y, q.z, q.w);
    public static implicit operator Quaternion(System.Numerics.Quaternion q) => new(q.X, q.Y, q.Z, q.W);

    public override bool Equals(object? obj) => obj is Quaternion q && this == q;

    public bool Equals(Quaternion other) => this == other;

    public override int GetHashCode()
        => HashCode.Combine(x, y, z, w);

    public override string ToString()
        => $"({x:F3}, {y:F3}, {z:F3}, {w:F3})";
}
