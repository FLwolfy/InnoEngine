using System;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;

namespace Inno.Core.Mathematics;

/// <summary>
/// Represents a mutable vector2 value with component-wise arithmetic semantics.
/// </summary>
[DataContract]
public struct Vector2 : IEquatable<Vector2>
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
    /// Creates a vector from explicit component values.
    /// </summary>
    /// <param name="x">
    /// The horizontal or first component.
    /// </param>
    /// <param name="y">
    /// The vertical or second component.
    /// </param>
    public Vector2(float x, float y)
    {
        this.x = x;
        this.y = y;
    }

    /// <summary>
    /// A value whose components are all zero.
    /// </summary>
    public static readonly Vector2 ZERO = new(0f, 0f);
    /// <summary>
    /// A value whose components are all one.
    /// </summary>
    public static readonly Vector2 ONE = new(1f, 1f);
    /// <summary>
    /// A unit value aligned with the x axis.
    /// </summary>
    public static readonly Vector2 UNIT_X = new(1f, 0f);
    /// <summary>
    /// A unit value aligned with the y axis.
    /// </summary>
    public static readonly Vector2 UNIT_Y = new(0f, 1f);

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
    public float LengthSquared() => SimdMath.Dot2(x, y, x, y);

    /// <summary>
    /// Gets a unit-length copy, or the zero value when normalization is undefined.
    /// </summary>
    public Vector2 normalized
    {
        get
        {
            float len = Length();
            return len > 0f ? this / len : ZERO;
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
    /// The validated vector2 that represents the completed operation.
    /// </returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector2 NormalizeSafe(Vector2 value, float epsilon = MathHelper.C_TOLERANCE)
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
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Dot(Vector2 a, Vector2 b) => SimdMath.Dot2(a.x, a.y, b.x, b.y);

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

    /// <summary>
    /// Calculates the signed angle in radians from one value to another.
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
    public static float SignedAngle(Vector2 from, Vector2 to)
    {
        float unsigned = Angle(from, to);
        float sign = MathF.Sign(from.x * to.y - from.y * to.x);
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
    /// The validated vector2 that represents the completed operation.
    /// </returns>
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
    /// The validated vector2 that represents the completed operation.
    /// </returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector2 Lerp(Vector2 a, Vector2 b, float t)
        => new Vector2(a.x + (b.x - a.x) * t, a.y + (b.y - a.y) * t);

    /// <summary>
    /// Selects the minimum value independently for each component.
    /// </summary>
    /// <param name="a">
    /// The first operand or interpolation endpoint.
    /// </param>
    /// <param name="b">
    /// The second operand or interpolation endpoint.
    /// </param>
    /// <returns>
    /// The validated vector2 that represents the completed operation.
    /// </returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector2 Min(Vector2 a, Vector2 b)
        => new Vector2(MathF.Min(a.x, b.x), MathF.Min(a.y, b.y));

    /// <summary>
    /// Selects the maximum value independently for each component.
    /// </summary>
    /// <param name="a">
    /// The first operand or interpolation endpoint.
    /// </param>
    /// <param name="b">
    /// The second operand or interpolation endpoint.
    /// </param>
    /// <returns>
    /// The validated vector2 that represents the completed operation.
    /// </returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector2 Max(Vector2 a, Vector2 b)
        => new Vector2(MathF.Max(a.x, b.x), MathF.Max(a.y, b.y));

    /// <summary>
    /// Reflects an incident value across the supplied normal.
    /// </summary>
    /// <param name="v">
    /// The concrete value read or transformed by this operation.
    /// </param>
    /// <param name="n">
    /// The surface normal used by the operation.
    /// </param>
    /// <returns>
    /// The validated vector2 that represents the completed operation.
    /// </returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector2 Reflect(Vector2 v, Vector2 n)
        => v - 2f * Dot(v, n) * n;
    
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
    /// The validated vector2 that represents the completed operation.
    /// </returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector2 Transform(Vector2 v, Matrix m)
    {
        float x = m.m11 * v.x + m.m12 * v.y + m.m14;
        float y = m.m21 * v.x + m.m22 * v.y + m.m24;
        return new Vector2(x, y);
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
    /// The validated vector2 that represents the completed operation.
    /// </returns>
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
    /// The validated vector2 that represents the completed operation.
    /// </returns>

    // Operators
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector2 operator +(Vector2 a, Vector2 b) => new Vector2(a.x + b.x, a.y + b.y);
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
    /// The validated vector2 that represents the completed operation.
    /// </returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector2 operator -(Vector2 a, Vector2 b) => new Vector2(a.x - b.x, a.y - b.y);
    /// <summary>
    /// Subtracts or negates the supplied value component by component.
    /// </summary>
    /// <param name="v">
    /// The concrete value read or transformed by this operation.
    /// </param>
    /// <returns>
    /// The validated vector2 that represents the completed operation.
    /// </returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector2 operator -(Vector2 v) => new Vector2(-v.x, -v.y);
    /// <summary>
    /// Multiplies the supplied values according to their algebraic contract.
    /// </summary>
    /// <param name="v">
    /// The concrete value read or transformed by this operation.
    /// </param>
    /// <param name="scalar">
    /// The scalar applied to every component.
    /// </param>
    /// <returns>
    /// The validated vector2 that represents the completed operation.
    /// </returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector2 operator *(Vector2 v, float scalar) => new Vector2(v.x * scalar, v.y * scalar);
    /// <summary>
    /// Multiplies the supplied values according to their algebraic contract.
    /// </summary>
    /// <param name="scalar">
    /// The scalar applied to every component.
    /// </param>
    /// <param name="v">
    /// The concrete value read or transformed by this operation.
    /// </param>
    /// <returns>
    /// The validated vector2 that represents the completed operation.
    /// </returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector2 operator *(float scalar, Vector2 v) => v * scalar;
    /// <summary>
    /// Divides the supplied value by the scalar divisor component by component.
    /// </summary>
    /// <param name="v">
    /// The concrete value read or transformed by this operation.
    /// </param>
    /// <param name="scalar">
    /// The scalar applied to every component.
    /// </param>
    /// <returns>
    /// The validated vector2 that represents the completed operation.
    /// </returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector2 operator /(Vector2 v, float scalar) => new Vector2(v.x / scalar, v.y / scalar);

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
    public static bool operator ==(Vector2 a, Vector2 b) => MathHelper.AlmostEquals(a.x, b.x) && MathHelper.AlmostEquals(a.y, b.y);
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
    public static bool operator !=(Vector2 a, Vector2 b) => !(a == b);
    
    /// <summary>
    /// Converts the supplied value to <see cref="System.Numerics.Vector2"/>.
    /// </summary>
    /// <param name="v">
    /// The concrete value read or transformed by this operation.
    /// </param>
    /// <returns>
    /// The validated system.numerics.vector2 that represents the completed operation.
    /// </returns>
    public static implicit operator System.Numerics.Vector2(Vector2 v) => new(v.x, v.y);
    /// <summary>
    /// Converts the supplied value to <see cref="Vector2"/>.
    /// </summary>
    /// <param name="v">
    /// The concrete value read or transformed by this operation.
    /// </param>
    /// <returns>
    /// The validated vector2 that represents the completed operation.
    /// </returns>
    public static implicit operator Vector2(System.Numerics.Vector2 v) => new(v.X, v.Y);

    /// <summary>
    /// Determines whether this value and the supplied value represent the same logical state.
    /// </summary>
    /// <param name="obj">
    /// The object compared with this value.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the requested condition is satisfied; otherwise, <see langword="false"/>.
    /// </returns>
    public override bool Equals(object? obj) => obj is Vector2 other && Equals(other);
    /// <summary>
    /// Determines whether this value and the supplied value represent the same logical state.
    /// </summary>
    /// <param name="other">
    /// The strongly typed value compared with this value.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the requested condition is satisfied; otherwise, <see langword="false"/>.
    /// </returns>
    public bool Equals(Vector2 other) => MathHelper.AlmostEquals(x, other.x) && MathHelper.AlmostEquals(y, other.y);
    /// <summary>
    /// Computes a hash code consistent with the implemented equality contract.
    /// </summary>
    /// <returns>
    /// The scalar result calculated from the supplied inputs.
    /// </returns>
    public override int GetHashCode() => HashCode.Combine(x, y);
    /// <summary>
    /// Formats this value as a human-readable component list.
    /// </summary>
    /// <returns>
    /// The validated text representation owned by the caller.
    /// </returns>
    public override string ToString() => $"({x:F2}, {y:F2})";
}
