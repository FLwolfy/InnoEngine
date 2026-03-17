using System;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;

namespace Inno.Core.Mathematics;

[DataContract]
public struct Vector4Int : IEquatable<Vector4Int>
{
    [DataMember] public int x;
    [DataMember] public int y;
    [DataMember] public int z;
    [DataMember] public int w;

    public Vector4Int(int x, int y, int z, int w)
    {
        this.x = x;
        this.y = y;
        this.z = z;
        this.w = w;
    }

    // Common vectors
    public static readonly Vector4Int ZERO   = new(0, 0, 0, 0);
    public static readonly Vector4Int ONE    = new(1, 1, 1, 1);
    public static readonly Vector4Int UNIT_X = new(1, 0, 0, 0);
    public static readonly Vector4Int UNIT_Y = new(0, 1, 0, 0);
    public static readonly Vector4Int UNIT_Z = new(0, 0, 1, 0);
    public static readonly Vector4Int UNIT_W = new(0, 0, 0, 1);

    // Dot product
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Dot(Vector4Int a, Vector4Int b) =>
        a.x * b.x + a.y * b.y + a.z * b.z + a.w * b.w;

    // Lerp (integer lerp)
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector4Int Lerp(Vector4Int a, Vector4Int b, float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return new Vector4Int(
            (int)(a.x + (b.x - a.x) * t),
            (int)(a.y + (b.y - a.y) * t),
            (int)(a.z + (b.z - a.z) * t),
            (int)(a.w + (b.w - a.w) * t)
        );
    }

    // Reflect
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector4Int Reflect(Vector4Int vec, Vector4Int normal)
    {
        int dot = Dot(vec, normal);
        return vec - 2 * dot * normal;
    }

    // Transform by Matrix (column vector)
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector4Int Transform(Vector4Int v, Matrix m)
    {
        int tx = (int)(m.m11 * v.x + m.m12 * v.y + m.m13 * v.z + m.m14 * v.w);
        int ty = (int)(m.m21 * v.x + m.m22 * v.y + m.m23 * v.z + m.m24 * v.w);
        int tz = (int)(m.m31 * v.x + m.m32 * v.y + m.m33 * v.z + m.m34 * v.w);
        int tw = (int)(m.m41 * v.x + m.m42 * v.y + m.m43 * v.z + m.m44 * v.w);
        return new Vector4Int(tx, ty, tz, tw);
    }

    // Operators
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector4Int operator +(Vector4Int a, Vector4Int b) =>
        new(a.x + b.x, a.y + b.y, a.z + b.z, a.w + b.w);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector4Int operator -(Vector4Int a, Vector4Int b) =>
        new(a.x - b.x, a.y - b.y, a.z - b.z, a.w - b.w);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector4Int operator -(Vector4Int v) =>
        new(-v.x, -v.y, -v.z, -v.w);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector4Int operator *(Vector4Int v, int s) =>
        new(v.x * s, v.y * s, v.z * s, v.w * s);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector4Int operator *(int s, Vector4Int v) => v * s;
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector4Int operator /(Vector4Int v, int s) =>
        new(v.x / s, v.y / s, v.z / s, v.w / s);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator ==(Vector4Int a, Vector4Int b) =>
        a.x == b.x && a.y == b.y && a.z == b.z && a.w == b.w;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator !=(Vector4Int a, Vector4Int b) => !(a == b);

    // Conversions
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static explicit operator Vector4(Vector4Int v) =>
        new(v.x, v.y, v.z, v.w);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static explicit operator Vector4Int(Vector4 v) =>
        new((int)v.x, (int)v.y, (int)v.z, (int)v.w);

    public override bool Equals(object? obj) => obj is Vector4Int other && Equals(other);
    public bool Equals(Vector4Int other) => this == other;
    public override int GetHashCode() => HashCode.Combine(x, y, z, w);
    public override string ToString() => $"({x}, {y}, {z}, {w})";
}
