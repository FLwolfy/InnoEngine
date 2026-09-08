using System;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.Arm;
using System.Runtime.Intrinsics.X86;

namespace Inno.Core.Mathematics;

/// <summary>
/// Represents a mutable vector4 value with component-wise arithmetic semantics.
/// </summary>
[DataContract]
public struct Vector4 : IEquatable<Vector4>
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
    /// <param name="w">
    /// The homogeneous or fourth component.
    /// </param>
    public Vector4(float x, float y, float z, float w)
    {
        this.x = x;
        this.y = y;
        this.z = z;
        this.w = w;
    }
    /// <summary>
    /// A value whose components are all zero.
    /// </summary>

    // Common vectors
    public static readonly Vector4 ZERO = new(0, 0, 0, 0);
    /// <summary>
    /// A value whose components are all one.
    /// </summary>
    public static readonly Vector4 ONE = new(1, 1, 1, 1);
    /// <summary>
    /// A unit value aligned with the x axis.
    /// </summary>
    public static readonly Vector4 UNIT_X = new(1, 0, 0, 0);
    /// <summary>
    /// A unit value aligned with the y axis.
    /// </summary>
    public static readonly Vector4 UNIT_Y = new(0, 1, 0, 0);
    /// <summary>
    /// A unit value aligned with the z axis.
    /// </summary>
    public static readonly Vector4 UNIT_Z = new(0, 0, 1, 0);
    /// <summary>
    /// A unit value aligned with the w axis.
    /// </summary>
    public static readonly Vector4 UNIT_W = new(0, 0, 0, 1);
    /// <summary>
    /// Calculates the Euclidean magnitude of this value.
    /// </summary>
    /// <returns>
    /// The scalar result calculated from the supplied inputs.
    /// </returns>

    // Length
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
    public Vector4 normalized
    {
        get
        {
            float len = Length();
            return len > 0 ? this / len : ZERO;
        }
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
    public static float Dot(Vector4 a, Vector4 b) =>
        SimdMath.Dot4(a.x, a.y, a.z, a.w, b.x, b.y, b.z, b.w);
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
    /// The validated vector4 that represents the completed operation.
    /// </returns>

    // Lerp
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector4 Lerp(Vector4 a, Vector4 b, float t) =>
        a + (b - a) * Math.Clamp(t, 0f, 1f);
    /// <summary>
    /// Reflects an incident value across the supplied normal.
    /// </summary>
    /// <param name="vec">
    /// The vec consumed by reflect; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <param name="normal">
    /// The surface normal used by the operation.
    /// </param>
    /// <returns>
    /// The validated vector4 that represents the completed operation.
    /// </returns>

    // Reflect
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector4 Reflect(Vector4 vec, Vector4 normal) =>
        vec - 2f * Dot(vec, normal) * normal;
    /// <summary>
    /// Transforms the supplied value by the requested transformation.
    /// </summary>
    /// <param name="v">
    /// The concrete value read or transformed by this operation.
    /// </param>
    /// <param name="m">
    /// The transformation matrix applied to the supplied value.
    /// </param>
    /// <returns>
    /// The validated vector4 that represents the completed operation.
    /// </returns>

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
    /// <summary>
    /// Projects the supplied value onto the requested dimensional space.
    /// </summary>
    /// <returns>
    /// The validated vector3 that represents the completed operation.
    /// </returns>

    // Project (useful in homogeneous coordinate systems)
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Vector3 ProjectToVector3()
    {
        if (w == 0f) return new Vector3(x, y, z); // Avoid division by zero
        return new Vector3(x / w, y / w, z / w);
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
    /// The validated vector4 that represents the completed operation.
    /// </returns>

    // Operators
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector4 operator +(Vector4 a, Vector4 b) =>
        new(a.x + b.x, a.y + b.y, a.z + b.z, a.w + b.w);

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
    /// The validated vector4 that represents the completed operation.
    /// </returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector4 operator -(Vector4 a, Vector4 b) =>
        new(a.x - b.x, a.y - b.y, a.z - b.z, a.w - b.w);

    /// <summary>
    /// Subtracts or negates the supplied value component by component.
    /// </summary>
    /// <param name="v">
    /// The concrete value read or transformed by this operation.
    /// </param>
    /// <returns>
    /// The validated vector4 that represents the completed operation.
    /// </returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector4 operator -(Vector4 v) =>
        new(-v.x, -v.y, -v.z, -v.w);

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
    /// The validated vector4 that represents the completed operation.
    /// </returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector4 operator *(Vector4 v, float s) =>
        new(v.x * s, v.y * s, v.z * s, v.w * s);

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
    /// The validated vector4 that represents the completed operation.
    /// </returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector4 operator *(float s, Vector4 v) => v * s;
    
    /// <summary>
    /// Multiplies the supplied values according to their algebraic contract.
    /// </summary>
    /// <param name="m">
    /// The transformation matrix applied to the supplied value.
    /// </param>
    /// <param name="v">
    /// The concrete value read or transformed by this operation.
    /// </param>
    /// <returns>
    /// The validated vector4 that represents the completed operation.
    /// </returns>
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
    /// The validated vector4 that represents the completed operation.
    /// </returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector4 operator /(Vector4 v, float s) =>
        new(v.x / s, v.y / s, v.z / s, v.w / s);

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
    public static bool operator ==(Vector4 a, Vector4 b) =>
        MathHelper.AlmostEquals(a.x, b.x) &&
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
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator !=(Vector4 a, Vector4 b) => !(a == b);
    
    /// <summary>
    /// Converts the supplied value to <see cref="System.Numerics.Vector4"/>.
    /// </summary>
    /// <param name="v">
    /// The concrete value read or transformed by this operation.
    /// </param>
    /// <returns>
    /// The validated system.numerics.vector4 that represents the completed operation.
    /// </returns>
    public static implicit operator System.Numerics.Vector4(Vector4 v) => new(v.x, v.y, v.z, v.w);
    /// <summary>
    /// Converts the supplied value to <see cref="Vector4"/>.
    /// </summary>
    /// <param name="v">
    /// The concrete value read or transformed by this operation.
    /// </param>
    /// <returns>
    /// The validated vector4 that represents the completed operation.
    /// </returns>
    public static implicit operator Vector4(System.Numerics.Vector4 v) => new(v.X, v.Y, v.Z, v.W);

    /// <summary>
    /// Determines whether this value and the supplied value represent the same logical state.
    /// </summary>
    /// <param name="obj">
    /// The object compared with this value.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the requested condition is satisfied; otherwise, <see langword="false"/>.
    /// </returns>
    public override bool Equals(object? obj) => obj is Vector4 other && this == other;
    /// <summary>
    /// Determines whether this value and the supplied value represent the same logical state.
    /// </summary>
    /// <param name="other">
    /// The strongly typed value compared with this value.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the requested condition is satisfied; otherwise, <see langword="false"/>.
    /// </returns>
    public bool Equals(Vector4 other) => this == other;
    /// <summary>
    /// Computes a hash code consistent with the implemented equality contract.
    /// </summary>
    /// <returns>
    /// The scalar result calculated from the supplied inputs.
    /// </returns>
    public override int GetHashCode() => HashCode.Combine(x, y, z, w);
    /// <summary>
    /// Formats this value as a human-readable component list.
    /// </summary>
    /// <returns>
    /// The validated text representation owned by the caller.
    /// </returns>
    public override string ToString() => $"({x}, {y}, {z}, {w})";
}
