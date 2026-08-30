using System;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;

namespace Inno.Core.Mathematics;

[DataContract]
public struct Vector2 : IEquatable<Vector2>
{
    [DataMember] public float x;
    [DataMember] public float y;

    public Vector2(float x, float y)
    {
        this.x = x;
        this.y = y;
    }

    public static readonly Vector2 ZERO = new(0f, 0f);
    public static readonly Vector2 ONE = new(1f, 1f);
    public static readonly Vector2 UNIT_X = new(1f, 0f);
    public static readonly Vector2 UNIT_Y = new(0f, 1f);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public float Length() => MathF.Sqrt(LengthSquared());

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public float LengthSquared() => SimdMath.Dot2(x, y, x, y);

    public Vector2 normalized
    {
        get
        {
            float len = Length();
            return len > 0f ? this / len : ZERO;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector2 NormalizeSafe(Vector2 value, float epsilon = MathHelper.C_TOLERANCE)
    {
        float len = value.Length();
        return len > epsilon ? value / len : ZERO;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Dot(Vector2 a, Vector2 b) => SimdMath.Dot2(a.x, a.y, b.x, b.y);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Angle(Vector2 from, Vector2 to)
    {
        float denom = from.Length() * to.Length();
        if (denom <= MathHelper.C_TOLERANCE)
        {
            return 0f;
        }

        float cos = Dot(from, to) / denom;
        cos = Math.Clamp(cos, -1f, 1f);
        return MathF.Acos(cos);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float SignedAngle(Vector2 from, Vector2 to)
    {
        float unsigned = Angle(from, to);
        float sign = MathF.Sign(from.x * to.y - from.y * to.x);
        return unsigned * sign;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector2 Project(Vector2 vector, Vector2 onto)
    {
        float denom = Dot(onto, onto);
        if (denom <= MathHelper.C_TOLERANCE)
        {
            return ZERO;
        }

        return onto * (Dot(vector, onto) / denom);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector2 Lerp(Vector2 a, Vector2 b, float t)
        => new Vector2(a.x + (b.x - a.x) * t, a.y + (b.y - a.y) * t);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector2 Min(Vector2 a, Vector2 b)
        => new Vector2(MathF.Min(a.x, b.x), MathF.Min(a.y, b.y));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector2 Max(Vector2 a, Vector2 b)
        => new Vector2(MathF.Max(a.x, b.x), MathF.Max(a.y, b.y));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector2 Reflect(Vector2 v, Vector2 n)
        => v - 2f * Dot(v, n) * n;
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector2 Transform(Vector2 v, Matrix m)
    {
        float x = m.m11 * v.x + m.m12 * v.y + m.m14;
        float y = m.m21 * v.x + m.m22 * v.y + m.m24;
        return new Vector2(x, y);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector2 Transform(Vector2 value, Quaternion rotation)
    {
        float x = rotation.x;
        float y = rotation.y;
        float z = rotation.z;
        float w = rotation.w;
        return new Vector2(
            value.x * (1f - 2f * (y * y + z * z))
                + value.y * (2f * (x * y - z * w)),
            value.x * (2f * (x * y + z * w))
                + value.y * (1f - 2f * (x * x + z * z))
        );
    }

    // Operators
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector2 operator +(Vector2 a, Vector2 b) => new Vector2(a.x + b.x, a.y + b.y);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector2 operator -(Vector2 a, Vector2 b) => new Vector2(a.x - b.x, a.y - b.y);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector2 operator -(Vector2 v) => new Vector2(-v.x, -v.y);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector2 operator *(Vector2 v, float scalar) => new Vector2(v.x * scalar, v.y * scalar);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector2 operator *(float scalar, Vector2 v) => v * scalar;
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector2 operator /(Vector2 v, float scalar) => new Vector2(v.x / scalar, v.y / scalar);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator ==(Vector2 a, Vector2 b) => MathHelper.AlmostEquals(a.x, b.x) && MathHelper.AlmostEquals(a.y, b.y);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator !=(Vector2 a, Vector2 b) => !(a == b);
    
    public static implicit operator System.Numerics.Vector2(Vector2 v) => new(v.x, v.y);
    public static implicit operator Vector2(System.Numerics.Vector2 v) => new(v.X, v.Y);

    public override bool Equals(object? obj) => obj is Vector2 other && Equals(other);
    public bool Equals(Vector2 other) => MathHelper.AlmostEquals(x, other.x) && MathHelper.AlmostEquals(y, other.y);
    public override int GetHashCode() => HashCode.Combine(x, y);
    public override string ToString() => $"({x:F2}, {y:F2})";
}
