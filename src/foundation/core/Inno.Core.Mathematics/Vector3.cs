using System;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.Arm;
using System.Runtime.Intrinsics.X86;

namespace Inno.Core.Mathematics;

/// <summary>
/// Represents a mutable vector3 value with component-wise arithmetic semantics.
/// </summary>
[DataContract]
public struct Vector3 : IEquatable<Vector3>
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
    /// Creates a vector from explicit component values.
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
    public Vector3(float x, float y, float z)
    {
        this.x = x;
        this.y = y;
        this.z = z;
    }
    /// <summary>
    /// A value whose components are all zero.
    /// </summary>

    // Common static vectors
    public static readonly Vector3 ZERO = new(0, 0, 0);
    /// <summary>
    /// A value whose components are all one.
    /// </summary>
    public static readonly Vector3 ONE = new(1, 1, 1);
    /// <summary>
    /// The up value used as part of this type's public representation.
    /// </summary>
    public static readonly Vector3 UP = new(0, 1, 0);
    /// <summary>
    /// The down value used as part of this type's public representation.
    /// </summary>
    public static readonly Vector3 DOWN = new(0, -1, 0);
    /// <summary>
    /// The left value used as part of this type's public representation.
    /// </summary>
    public static readonly Vector3 LEFT = new(-1, 0, 0);
    /// <summary>
    /// The right value used as part of this type's public representation.
    /// </summary>
    public static readonly Vector3 RIGHT = new(1, 0, 0);
    /// <summary>
    /// The forward value used as part of this type's public representation.
    /// </summary>
    public static readonly Vector3 FORWARD = new(0, 0, 1);
    /// <summary>
    /// The back value used as part of this type's public representation.
    /// </summary>
    public static readonly Vector3 BACK = new(0, 0, -1);
    /// <summary>
    /// Calculates the Euclidean magnitude of this value.
    /// </summary>
    /// <returns>
    /// The scalar result calculated from the supplied inputs.
    /// </returns>

    // Lengths
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public float Length() => MathF.Sqrt(LengthSquared());

    /// <summary>
    /// Calculates the squared Euclidean magnitude without a square-root operation.
    /// </summary>
    /// <returns>
    /// The scalar result calculated from the supplied inputs.
    /// </returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public float LengthSquared() => SimdMath.Dot3(x, y, z, x, y, z);

    /// <summary>
    /// Gets a unit-length copy, or the zero value when normalization is undefined.
    /// </summary>
    public Vector3 normalized
    {
        get
        {
            float len = Length();
            return len > 0 ? this / len : ZERO;
        }
    }

    /// <summary>
    /// Returns a unit-length value while handling degenerate input according to the method contract.
    /// </summary>
    /// <param name="value">
    /// The concrete value read or transformed by this operation.
    /// </param>
    /// <param name="epsilon">
    /// The positive tolerance below which input is treated as degenerate.
    /// </param>
    /// <returns>
    /// The validated vector3 that represents the completed operation.
    /// </returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector3 NormalizeSafe(Vector3 value, float epsilon = MathHelper.C_TOLERANCE)
    {
        float len = value.Length();
        return len > epsilon ? value / len : ZERO;
    }
    /// <summary>
    /// Calculates the scalar dot product of two values.
    /// </summary>
    /// <param name="a">
    /// The first operand or interpolation endpoint.
    /// </param>
    /// <param name="b">
    /// The second operand or interpolation endpoint.
    /// </param>
    /// <returns>
    /// The scalar result calculated from the supplied inputs.
    /// </returns>

    // Dot product
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Dot(Vector3 a, Vector3 b) => SimdMath.Dot3(a.x, a.y, a.z, b.x, b.y, b.z);

    /// <summary>
    /// Calculates the unsigned angle in radians between two values.
    /// </summary>
    /// <param name="from">
    /// The starting value used by the operation.
    /// </param>
    /// <param name="to">
    /// The destination value used by the operation.
    /// </param>
    /// <returns>
    /// The scalar result calculated from the supplied inputs.
    /// </returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Angle(Vector3 from, Vector3 to)
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

    /// <summary>
    /// Calculates the signed angle in radians from one value to another.
    /// </summary>
    /// <param name="from">
    /// The starting value used by the operation.
    /// </param>
    /// <param name="to">
    /// The destination value used by the operation.
    /// </param>
    /// <param name="axis">
    /// The axis consumed by signed angle; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <returns>
    /// The scalar result calculated from the supplied inputs.
    /// </returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float SignedAngle(Vector3 from, Vector3 to, Vector3 axis)
    {
        float unsigned = Angle(from, to);
        Vector3 cross = Cross(from, to);
        float sign = MathF.Sign(Dot(axis, cross));
        return unsigned * sign;
    }

    /// <summary>
    /// Projects the supplied value onto the requested dimensional space.
    /// </summary>
    /// <param name="vector">
    /// The concrete value read or transformed by this operation.
    /// </param>
    /// <param name="onto">
    /// The onto consumed by project; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <returns>
    /// The validated vector3 that represents the completed operation.
    /// </returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector3 Project(Vector3 vector, Vector3 onto)
    {
        float denom = Dot(onto, onto);
        if (denom <= MathHelper.C_TOLERANCE)
        {
            return ZERO;
        }

        return onto * (Dot(vector, onto) / denom);
    }
    /// <summary>
    /// Calculates the vector perpendicular to both supplied vectors.
    /// </summary>
    /// <param name="a">
    /// The first operand or interpolation endpoint.
    /// </param>
    /// <param name="b">
    /// The second operand or interpolation endpoint.
    /// </param>
    /// <returns>
    /// The validated vector3 that represents the completed operation.
    /// </returns>

    // Cross product
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector3 Cross(Vector3 a, Vector3 b) => new(
        a.y * b.z - a.z * b.y,
        a.z * b.x - a.x * b.z,
        a.x * b.y - a.y * b.x
    );
    /// <summary>
    /// Calculates the Euclidean distance between two points.
    /// </summary>
    /// <param name="a">
    /// The first operand or interpolation endpoint.
    /// </param>
    /// <param name="b">
    /// The second operand or interpolation endpoint.
    /// </param>
    /// <returns>
    /// The scalar result calculated from the supplied inputs.
    /// </returns>

    // Distance
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Distance(Vector3 a, Vector3 b) => (a - b).Length();
    /// <summary>
    /// Interpolates linearly between two values without clamping the interpolation factor.
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
    /// The validated vector3 that represents the completed operation.
    /// </returns>

    // Lerp
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector3 Lerp(Vector3 a, Vector3 b, float t) =>
        a + (b - a) * Math.Clamp(t, 0f, 1f);
    /// <summary>
    /// Reflects an incident value across the supplied normal.
    /// </summary>
    /// <param name="dir">
    /// The dir consumed by reflect; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <param name="normal">
    /// The surface normal used by the operation.
    /// </param>
    /// <returns>
    /// The validated vector3 that represents the completed operation.
    /// </returns>

    // Reflect
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector3 Reflect(Vector3 dir, Vector3 normal) =>
        dir - 2f * Dot(dir, normal) * normal;
    
    /// <summary>
    /// The column vector transform. It performs as below m * v (not v * m).
    /// </summary>
    /// <returns></returns>
    /// <param name="position">
    /// The position consumed by transform; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <param name="matrix">
    /// The transformation matrix applied to the supplied value.
    /// </param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector3 Transform(Vector3 position, Matrix matrix)
    {
        if (Sse.IsSupported || AdvSimd.IsSupported)
        {
            var v = Vector128.Create(position.x, position.y, position.z, 1f);
            var row1 = Vector128.Create(matrix.m11, matrix.m12, matrix.m13, matrix.m14);
            var row2 = Vector128.Create(matrix.m21, matrix.m22, matrix.m23, matrix.m24);
            var row3 = Vector128.Create(matrix.m31, matrix.m32, matrix.m33, matrix.m34);

            float x = SimdMath.Dot4(row1, v);
            float y = SimdMath.Dot4(row2, v);
            float z = SimdMath.Dot4(row3, v);
            return new Vector3(x, y, z);
        }

        float tx = matrix.m11 * position.x + matrix.m12 * position.y + matrix.m13 * position.z + matrix.m14;
        float ty = matrix.m21 * position.x + matrix.m22 * position.y + matrix.m23 * position.z + matrix.m24;
        float tz = matrix.m31 * position.x + matrix.m32 * position.y + matrix.m33 * position.z + matrix.m34;
        return new Vector3(tx, ty, tz);
    }

    /// <summary>
    /// Transforms a normal by a matrix (ignores translation).
    /// </summary>
    /// <param name="normal">
    /// The surface normal used by the operation.
    /// </param>
    /// <param name="matrix">
    /// The transformation matrix applied to the supplied value.
    /// </param>
    /// <returns>
    /// The validated vector3 that represents the completed operation.
    /// </returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector3 TransformNormal(Vector3 normal, Matrix matrix)
    {
        if (Sse.IsSupported || AdvSimd.IsSupported)
        {
            var v = Vector128.Create(normal.x, normal.y, normal.z, 0f);
            var row1 = Vector128.Create(matrix.m11, matrix.m12, matrix.m13, 0f);
            var row2 = Vector128.Create(matrix.m21, matrix.m22, matrix.m23, 0f);
            var row3 = Vector128.Create(matrix.m31, matrix.m32, matrix.m33, 0f);

            float x = SimdMath.Dot4(row1, v);
            float y = SimdMath.Dot4(row2, v);
            float z = SimdMath.Dot4(row3, v);
            return new Vector3(x, y, z);
        }

        float tx = matrix.m11 * normal.x + matrix.m12 * normal.y + matrix.m13 * normal.z;
        float ty = matrix.m21 * normal.x + matrix.m22 * normal.y + matrix.m23 * normal.z;
        float tz = matrix.m31 * normal.x + matrix.m32 * normal.y + matrix.m33 * normal.z;
        return new Vector3(tx, ty, tz);
    }
    
    /// <summary>
    /// Transforms the supplied value by the requested transformation.
    /// </summary>
    /// <param name="value">
    /// The concrete value read or transformed by this operation.
    /// </param>
    /// <param name="rotation">
    /// The rotation applied to the supplied value.
    /// </param>
    /// <returns>
    /// The validated vector3 that represents the completed operation.
    /// </returns>
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
    /// <summary>
    /// Adds the supplied values component by component.
    /// </summary>
    /// <param name="a">
    /// The first operand or interpolation endpoint.
    /// </param>
    /// <param name="b">
    /// The second operand or interpolation endpoint.
    /// </param>
    /// <returns>
    /// The validated vector3 that represents the completed operation.
    /// </returns>

    // Operators
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector3 operator +(Vector3 a, Vector3 b) => new(a.x + b.x, a.y + b.y, a.z + b.z);
    /// <summary>
    /// Subtracts or negates the supplied value component by component.
    /// </summary>
    /// <param name="a">
    /// The first operand or interpolation endpoint.
    /// </param>
    /// <param name="b">
    /// The second operand or interpolation endpoint.
    /// </param>
    /// <returns>
    /// The validated vector3 that represents the completed operation.
    /// </returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector3 operator -(Vector3 a, Vector3 b) => new(a.x - b.x, a.y - b.y, a.z - b.z);
    /// <summary>
    /// Subtracts or negates the supplied value component by component.
    /// </summary>
    /// <param name="v">
    /// The concrete value read or transformed by this operation.
    /// </param>
    /// <returns>
    /// The validated vector3 that represents the completed operation.
    /// </returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector3 operator -(Vector3 v) => new(-v.x, -v.y, -v.z);
    /// <summary>
    /// Multiplies the supplied values according to their algebraic contract.
    /// </summary>
    /// <param name="v">
    /// The concrete value read or transformed by this operation.
    /// </param>
    /// <param name="s">
    /// The s consumed by *; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <returns>
    /// The validated vector3 that represents the completed operation.
    /// </returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector3 operator *(Vector3 v, float s) => new(v.x * s, v.y * s, v.z * s);
    /// <summary>
    /// Multiplies the supplied values according to their algebraic contract.
    /// </summary>
    /// <param name="s">
    /// The s consumed by *; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <param name="v">
    /// The concrete value read or transformed by this operation.
    /// </param>
    /// <returns>
    /// The validated vector3 that represents the completed operation.
    /// </returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector3 operator *(float s, Vector3 v) => v * s;
    /// <summary>
    /// Divides the supplied value by the scalar divisor component by component.
    /// </summary>
    /// <param name="v">
    /// The concrete value read or transformed by this operation.
    /// </param>
    /// <param name="s">
    /// The s consumed by /; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <returns>
    /// The validated vector3 that represents the completed operation.
    /// </returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector3 operator /(Vector3 v, float s) => new(v.x / s, v.y / s, v.z / s);

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
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator ==(Vector3 a, Vector3 b) =>
        MathHelper.AlmostEquals(a.x, b.x) &&
        MathHelper.AlmostEquals(a.y, b.y) &&
        MathHelper.AlmostEquals(a.z, b.z);

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
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator !=(Vector3 a, Vector3 b) => !(a == b);
    
    /// <summary>
    /// Converts the supplied value to <see cref="System.Numerics.Vector3"/>.
    /// </summary>
    /// <param name="v">
    /// The concrete value read or transformed by this operation.
    /// </param>
    /// <returns>
    /// The validated system.numerics.vector3 that represents the completed operation.
    /// </returns>
    public static implicit operator System.Numerics.Vector3(Vector3 v) => new(v.x, v.y, v.z);
    /// <summary>
    /// Converts the supplied value to <see cref="Vector3"/>.
    /// </summary>
    /// <param name="v">
    /// The concrete value read or transformed by this operation.
    /// </param>
    /// <returns>
    /// The validated vector3 that represents the completed operation.
    /// </returns>
    public static implicit operator Vector3(System.Numerics.Vector3 v) => new(v.X, v.Y, v.Z);

    /// <summary>
    /// Determines whether this value and the supplied value represent the same logical state.
    /// </summary>
    /// <param name="obj">
    /// The object compared with this value.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the requested condition is satisfied; otherwise, <see langword="false"/>.
    /// </returns>
    public override bool Equals(object? obj) => obj is Vector3 other && this == other;
    /// <summary>
    /// Determines whether this value and the supplied value represent the same logical state.
    /// </summary>
    /// <param name="other">
    /// The strongly typed value compared with this value.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the requested condition is satisfied; otherwise, <see langword="false"/>.
    /// </returns>
    public bool Equals(Vector3 other) => this == other;
    /// <summary>
    /// Computes a hash code consistent with the implemented equality contract.
    /// </summary>
    /// <returns>
    /// The scalar result calculated from the supplied inputs.
    /// </returns>
    public override int GetHashCode() => HashCode.Combine(x, y, z);
    /// <summary>
    /// Formats this value as a human-readable component list.
    /// </summary>
    /// <returns>
    /// The validated text representation owned by the caller.
    /// </returns>
    public override string ToString() => $"({x}, {y}, {z})";
}
