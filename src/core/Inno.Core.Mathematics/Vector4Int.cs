using System;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;

namespace Inno.Core.Mathematics;

/// <summary>
/// Represents a mutable vector4int value with component-wise arithmetic semantics.
/// </summary>
[DataContract]
public struct Vector4Int : IEquatable<Vector4Int>
{
    /// <summary>
    /// The horizontal or first component.
    /// </summary>
    [DataMember] public int x;
    /// <summary>
    /// The vertical or second component.
    /// </summary>
    [DataMember] public int y;
    /// <summary>
    /// The depth or third component.
    /// </summary>
    [DataMember] public int z;
    /// <summary>
    /// The homogeneous or fourth component.
    /// </summary>
    [DataMember] public int w;

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
    public Vector4Int(int x, int y, int z, int w)
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
    public static readonly Vector4Int ZERO   = new(0, 0, 0, 0);
    /// <summary>
    /// A value whose components are all one.
    /// </summary>
    public static readonly Vector4Int ONE    = new(1, 1, 1, 1);
    /// <summary>
    /// A unit value aligned with the x axis.
    /// </summary>
    public static readonly Vector4Int UNIT_X = new(1, 0, 0, 0);
    /// <summary>
    /// A unit value aligned with the y axis.
    /// </summary>
    public static readonly Vector4Int UNIT_Y = new(0, 1, 0, 0);
    /// <summary>
    /// A unit value aligned with the z axis.
    /// </summary>
    public static readonly Vector4Int UNIT_Z = new(0, 0, 1, 0);
    /// <summary>
    /// A unit value aligned with the w axis.
    /// </summary>
    public static readonly Vector4Int UNIT_W = new(0, 0, 0, 1);
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
    public static int Dot(Vector4Int a, Vector4Int b) =>
        a.x * b.x + a.y * b.y + a.z * b.z + a.w * b.w;
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
    /// The validated vector4int that represents the completed operation.
    /// </returns>

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
    /// The validated vector4int that represents the completed operation.
    /// </returns>

    // Reflect
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector4Int Reflect(Vector4Int vec, Vector4Int normal)
    {
        int dot = Dot(vec, normal);
        return vec - 2 * dot * normal;
    }
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
    /// The validated vector4int that represents the completed operation.
    /// </returns>

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
    /// The validated vector4int that represents the completed operation.
    /// </returns>

    // Operators
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector4Int operator +(Vector4Int a, Vector4Int b) =>
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
    /// The validated vector4int that represents the completed operation.
    /// </returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector4Int operator -(Vector4Int a, Vector4Int b) =>
        new(a.x - b.x, a.y - b.y, a.z - b.z, a.w - b.w);

    /// <summary>
    /// Subtracts or negates the supplied value component by component.
    /// </summary>
    /// <param name="v">
    /// The concrete value read or transformed by this operation.
    /// </param>
    /// <returns>
    /// The validated vector4int that represents the completed operation.
    /// </returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector4Int operator -(Vector4Int v) =>
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
    /// The validated vector4int that represents the completed operation.
    /// </returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector4Int operator *(Vector4Int v, int s) =>
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
    /// The validated vector4int that represents the completed operation.
    /// </returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector4Int operator *(int s, Vector4Int v) => v * s;
    
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
    /// The validated vector4int that represents the completed operation.
    /// </returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector4Int operator /(Vector4Int v, int s) =>
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
    public static bool operator ==(Vector4Int a, Vector4Int b) =>
        a.x == b.x && a.y == b.y && a.z == b.z && a.w == b.w;

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
    public static bool operator !=(Vector4Int a, Vector4Int b) => !(a == b);
    /// <summary>
    /// Converts the supplied value to <see cref="Vector4"/>.
    /// </summary>
    /// <param name="v">
    /// The concrete value read or transformed by this operation.
    /// </param>
    /// <returns>
    /// The validated vector4 that represents the completed operation.
    /// </returns>

    // Conversions
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static explicit operator Vector4(Vector4Int v) =>
        new(v.x, v.y, v.z, v.w);

    /// <summary>
    /// Converts the supplied value to <see cref="Vector4Int"/>.
    /// </summary>
    /// <param name="v">
    /// The concrete value read or transformed by this operation.
    /// </param>
    /// <returns>
    /// The validated vector4int that represents the completed operation.
    /// </returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static explicit operator Vector4Int(Vector4 v) =>
        new((int)v.x, (int)v.y, (int)v.z, (int)v.w);

    /// <summary>
    /// Determines whether this value and the supplied value represent the same logical state.
    /// </summary>
    /// <param name="obj">
    /// The object compared with this value.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the requested condition is satisfied; otherwise, <see langword="false"/>.
    /// </returns>
    public override bool Equals(object? obj) => obj is Vector4Int other && Equals(other);
    /// <summary>
    /// Determines whether this value and the supplied value represent the same logical state.
    /// </summary>
    /// <param name="other">
    /// The strongly typed value compared with this value.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the requested condition is satisfied; otherwise, <see langword="false"/>.
    /// </returns>
    public bool Equals(Vector4Int other) => this == other;
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
