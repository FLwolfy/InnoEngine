using System;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;

namespace Inno.Core.Mathematics;

/// <summary>
/// Represents a mutable vector2int value with component-wise arithmetic semantics.
/// </summary>
[DataContract]
public struct Vector2Int : IEquatable<Vector2Int>
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
    /// Creates a vector from explicit component values.
    /// </summary>
    /// <param name="x">
    /// The horizontal or first component.
    /// </param>
    /// <param name="y">
    /// The vertical or second component.
    /// </param>
    public Vector2Int(int x, int y)
    {
        this.x = x;
        this.y = y;
    }
    /// <summary>
    /// A value whose components are all zero.
    /// </summary>

    // Common constants
    public static readonly Vector2Int ZERO = new(0, 0);
    /// <summary>
    /// A value whose components are all one.
    /// </summary>
    public static readonly Vector2Int ONE  = new(1, 1);
    /// <summary>
    /// A unit value aligned with the x axis.
    /// </summary>
    public static readonly Vector2Int UNIT_X  = new(1, 0);
    /// <summary>
    /// A unit value aligned with the y axis.
    /// </summary>
    public static readonly Vector2Int UNIT_Y  = new(0, 1);
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
    /// The validated vector2int that represents the completed operation.
    /// </returns>

    // Operators
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector2Int operator +(Vector2Int a, Vector2Int b)
        => new(a.x + b.x, a.y + b.y);

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
    /// The validated vector2int that represents the completed operation.
    /// </returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector2Int operator -(Vector2Int a, Vector2Int b)
        => new(a.x - b.x, a.y - b.y);

    /// <summary>
    /// Subtracts or negates the supplied value component by component.
    /// </summary>
    /// <param name="v">
    /// The concrete value read or transformed by this operation.
    /// </param>
    /// <returns>
    /// The validated vector2int that represents the completed operation.
    /// </returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector2Int operator -(Vector2Int v)
        => new(-v.x, -v.y);

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
    /// The validated vector2int that represents the completed operation.
    /// </returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector2Int operator *(Vector2Int v, int scalar)
        => new(v.x * scalar, v.y * scalar);

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
    /// The validated vector2int that represents the completed operation.
    /// </returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector2Int operator *(int scalar, Vector2Int v)
        => v * scalar;

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
    /// The validated vector2int that represents the completed operation.
    /// </returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector2Int operator /(Vector2Int v, int scalar)
        => new(v.x / scalar, v.y / scalar);

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
    public static bool operator ==(Vector2Int a, Vector2Int b)
        => a.x == b.x && a.y == b.y;

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
    public static bool operator !=(Vector2Int a, Vector2Int b)
        => !(a == b);
    /// <summary>
    /// Converts the supplied value to <see cref="Vector2"/>.
    /// </summary>
    /// <param name="v">
    /// The concrete value read or transformed by this operation.
    /// </param>
    /// <returns>
    /// The validated vector2 that represents the completed operation.
    /// </returns>

    // Conversions
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static explicit operator Vector2(Vector2Int v)
        => new(v.x, v.y);

    /// <summary>
    /// Converts the supplied value to <see cref="Vector2Int"/>.
    /// </summary>
    /// <param name="v">
    /// The concrete value read or transformed by this operation.
    /// </param>
    /// <returns>
    /// The validated vector2int that represents the completed operation.
    /// </returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static explicit operator Vector2Int(Vector2 v)
        => new((int)v.x, (int)v.y);
    /// <summary>
    /// Determines whether this value and the supplied value represent the same logical state.
    /// </summary>
    /// <param name="obj">
    /// The object compared with this value.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the requested condition is satisfied; otherwise, <see langword="false"/>.
    /// </returns>

    // Equality
    public override bool Equals(object? obj)
        => obj is Vector2Int other && Equals(other);

    /// <summary>
    /// Determines whether this value and the supplied value represent the same logical state.
    /// </summary>
    /// <param name="other">
    /// The strongly typed value compared with this value.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the requested condition is satisfied; otherwise, <see langword="false"/>.
    /// </returns>
    public bool Equals(Vector2Int other)
        => x == other.x && y == other.y;

    /// <summary>
    /// Computes a hash code consistent with the implemented equality contract.
    /// </summary>
    /// <returns>
    /// The scalar result calculated from the supplied inputs.
    /// </returns>
    public override int GetHashCode()
        => HashCode.Combine(x, y);

    /// <summary>
    /// Formats this value as a human-readable component list.
    /// </summary>
    /// <returns>
    /// The validated text representation owned by the caller.
    /// </returns>
    public override string ToString()
        => $"({x}, {y})";
}
