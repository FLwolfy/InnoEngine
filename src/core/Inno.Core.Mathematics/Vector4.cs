using System;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.Arm;
using System.Runtime.Intrinsics.X86;

namespace Inno.Core.Mathematics;

[DataContract]
public struct Vector4 : IEquatable<Vector4>
{
    [DataMember] public float x;
    [DataMember] public float y;
    [DataMember] public float z;
    [DataMember] public float w;

    public Vector4(float x, float y, float z, float w)
    {
        this.x = x;
        this.y = y;
        this.z = z;
        this.w = w;
    }

    // Common vectors
    public static readonly Vector4 ZERO = new(0, 0, 0, 0);
    public static readonly Vector4 ONE = new(1, 1, 1, 1);
    public static readonly Vector4 UNIT_X = new(1, 0, 0, 0);
    public static readonly Vector4 UNIT_Y = new(0, 1, 0, 0);
    public static readonly Vector4 UNIT_Z = new(0, 0, 1, 0);
    public static readonly Vector4 UNIT_W = new(0, 0, 0, 1);

    // Length
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public float Length() => MathF.Sqrt(LengthSquared());

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public float LengthSquared() => SimdMath.Dot4(x, y, z, w, x, y, z, w);

    public Vector4 normalized
    {
        get
        {
            float len = Length();
            return len > 0 ? this / len : ZERO;
        }
    }

    // Dot product
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Dot(Vector4 a, Vector4 b) =>
        SimdMath.Dot4(a.x, a.y, a.z, a.w, b.x, b.y, b.z, b.w);

    // Lerp
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector4 Lerp(Vector4 a, Vector4 b, float t) =>
        a + (b - a) * Math.Clamp(t, 0f, 1f);

    // Reflect
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector4 Reflect(Vector4 vec, Vector4 normal) =>
        vec - 2f * Dot(vec, normal) * normal;

    // Transform by Matrix (assumes Vector4 is column vector)
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector4 Transform(Vector4 v, Matrix m)
    {
        if (Sse.IsSupported || AdvSimd.IsSupported)
        {
            var vec = Vector128.Create(v.x, v.y, v.z, v.w);
            var row1 = Vector128.Create(m.m11, m.m12, m.m13, m.m14);
            var row2 = Vector128.Create(m.m21, m.m22, m.m23, m.m24);
            var row3 = Vector128.Create(m.m31, m.m32, m.m33, m.m34);
            var row4 = Vector128.Create(m.m41, m.m42, m.m43, m.m44);

            float tx = SimdMath.Dot4(row1, vec);
            float ty = SimdMath.Dot4(row2, vec);
            float tz = SimdMath.Dot4(row3, vec);
            float tw = SimdMath.Dot4(row4, vec);
            return new Vector4(tx, ty, tz, tw);
        }

        float sx = m.m11 * v.x + m.m12 * v.y + m.m13 * v.z + m.m14 * v.w;
        float sy = m.m21 * v.x + m.m22 * v.y + m.m23 * v.z + m.m24 * v.w;
        float sz = m.m31 * v.x + m.m32 * v.y + m.m33 * v.z + m.m34 * v.w;
        float sw = m.m41 * v.x + m.m42 * v.y + m.m43 * v.z + m.m44 * v.w;
        return new Vector4(sx, sy, sz, sw);
    }

    // Project (useful in homogeneous coordinate systems)
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Vector3 ProjectToVector3()
    {
        if (w == 0f) return new Vector3(x, y, z); // Avoid division by zero
        return new Vector3(x / w, y / w, z / w);
    }

    // Operators
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector4 operator +(Vector4 a, Vector4 b) =>
        new(a.x + b.x, a.y + b.y, a.z + b.z, a.w + b.w);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector4 operator -(Vector4 a, Vector4 b) =>
        new(a.x - b.x, a.y - b.y, a.z - b.z, a.w - b.w);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector4 operator -(Vector4 v) =>
        new(-v.x, -v.y, -v.z, -v.w);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector4 operator *(Vector4 v, float s) =>
        new(v.x * s, v.y * s, v.z * s, v.w * s);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector4 operator *(float s, Vector4 v) => v * s;
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector4 operator *(Matrix m, Vector4 v)
    {
        return new Vector4(
            m.m11 * v.x + m.m12 * v.y + m.m13 * v.z + m.m14 * v.w,
            m.m21 * v.x + m.m22 * v.y + m.m23 * v.z + m.m24 * v.w,
            m.m31 * v.x + m.m32 * v.y + m.m33 * v.z + m.m34 * v.w,
            m.m41 * v.x + m.m42 * v.y + m.m43 * v.z + m.m44 * v.w
        );
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector4 operator /(Vector4 v, float s) =>
        new(v.x / s, v.y / s, v.z / s, v.w / s);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator ==(Vector4 a, Vector4 b) =>
        MathHelper.AlmostEquals(a.x, b.x) &&
        MathHelper.AlmostEquals(a.y, b.y) &&
        MathHelper.AlmostEquals(a.z, b.z) &&
        MathHelper.AlmostEquals(a.w, b.w);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator !=(Vector4 a, Vector4 b) => !(a == b);
    
    public static implicit operator System.Numerics.Vector4(Vector4 v) => new(v.x, v.y, v.z, v.w);
    public static implicit operator Vector4(System.Numerics.Vector4 v) => new(v.X, v.Y, v.Z, v.W);

    public override bool Equals(object? obj) => obj is Vector4 other && this == other;
    public bool Equals(Vector4 other) => this == other;
    public override int GetHashCode() => HashCode.Combine(x, y, z, w);
    public override string ToString() => $"({x}, {y}, {z}, {w})";
}
