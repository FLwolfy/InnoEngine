using System;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;

namespace Inno.Core.Mathematics;

[DataContract]
public struct Vector3 : IEquatable<Vector3>
{
    [DataMember] public float x;
    [DataMember] public float y;
    [DataMember] public float z;

    public Vector3(float x, float y, float z)
    {
        this.x = x;
        this.y = y;
        this.z = z;
    }

    // Common static vectors
    public static readonly Vector3 ZERO = new(0, 0, 0);
    public static readonly Vector3 ONE = new(1, 1, 1);
    public static readonly Vector3 UP = new(0, 1, 0);
    public static readonly Vector3 DOWN = new(0, -1, 0);
    public static readonly Vector3 LEFT = new(-1, 0, 0);
    public static readonly Vector3 RIGHT = new(1, 0, 0);
    public static readonly Vector3 FORWARD = new(0, 0, 1);
    public static readonly Vector3 BACK = new(0, 0, -1);

    // Lengths
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public float Length() => MathF.Sqrt(x * x + y * y + z * z);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public float LengthSquared() => x * x + y * y + z * z;

    public Vector3 normalized
    {
        get
        {
            float len = Length();
            return len > 0 ? this / len : ZERO;
        }
    }

    // Dot product
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Dot(Vector3 a, Vector3 b) => a.x * b.x + a.y * b.y + a.z * b.z;

    // Cross product
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector3 Cross(Vector3 a, Vector3 b) => new(
        a.y * b.z - a.z * b.y,
        a.z * b.x - a.x * b.z,
        a.x * b.y - a.y * b.x
    );

    // Distance
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Distance(Vector3 a, Vector3 b) => (a - b).Length();

    // Lerp
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector3 Lerp(Vector3 a, Vector3 b, float t) =>
        a + (b - a) * Math.Clamp(t, 0f, 1f);

    // Reflect
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector3 Reflect(Vector3 dir, Vector3 normal) =>
        dir - 2f * Dot(dir, normal) * normal;
    
    /// <summary>
    /// The column vector transform. It performs as below m * v (not v * m).
    /// </summary>
    /// <returns></returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector3 Transform(Vector3 position, Matrix matrix)
    {
        float x = matrix.m11 * position.x + matrix.m12 * position.y + matrix.m13 * position.z + matrix.m14;
        float y = matrix.m21 * position.x + matrix.m22 * position.y + matrix.m23 * position.z + matrix.m24;
        float z = matrix.m31 * position.x + matrix.m32 * position.y + matrix.m33 * position.z + matrix.m34;
        return new Vector3(x, y, z);
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector3 Transform(Vector3 value, Quaternion rotation)
    {
        float x = value.x, y = value.y, z = value.z;
        float qx = rotation.x, qy = rotation.y, qz = rotation.z, qw = rotation.w;

        float num1 = 2f * (qy * z - qz * y);
        float num2 = 2f * (qz * x - qx * z);
        float num3 = 2f * (qx * y - qy * x);

        float rx = x + num1 * qw + (qy * num3 - qz * num2);
        float ry = y + num2 * qw + (qz * num1 - qx * num3);
        float rz = z + num3 * qw + (qx * num2 - qy * num1);

        return new Vector3(rx, ry, rz);
    }

    // Operators
    public static Vector3 operator +(Vector3 a, Vector3 b) => new(a.x + b.x, a.y + b.y, a.z + b.z);
    public static Vector3 operator -(Vector3 a, Vector3 b) => new(a.x - b.x, a.y - b.y, a.z - b.z);
    public static Vector3 operator -(Vector3 v) => new(-v.x, -v.y, -v.z);
    public static Vector3 operator *(Vector3 v, float s) => new(v.x * s, v.y * s, v.z * s);
    public static Vector3 operator *(float s, Vector3 v) => v * s;
    public static Vector3 operator /(Vector3 v, float s) => new(v.x / s, v.y / s, v.z / s);

    public static bool operator ==(Vector3 a, Vector3 b) =>
        MathHelper.AlmostEquals(a.x, b.x) &&
        MathHelper.AlmostEquals(a.y, b.y) &&
        MathHelper.AlmostEquals(a.z, b.z);

    public static bool operator !=(Vector3 a, Vector3 b) => !(a == b);
    
    public static implicit operator System.Numerics.Vector3(Vector3 v) => new(v.x, v.y, v.z);
    public static implicit operator Vector3(System.Numerics.Vector3 v) => new(v.X, v.Y, v.Z);

    public override bool Equals(object? obj) => obj is Vector3 other && this == other;
    public bool Equals(Vector3 other) => this == other;
    public override int GetHashCode() => HashCode.Combine(x, y, z);
    public override string ToString() => $"({x}, {y}, {z})";
}
