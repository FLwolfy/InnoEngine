using System;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;

namespace Inno.Core.Mathematics;

/// <summary>
/// Represents a mutable vector3int value with component-wise arithmetic semantics.
/// </summary>
[DataContract]
public struct Vector3Int : IEquatable<Vector3Int>
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
    public Vector3Int(int x, int y, int z)
    {
        this.x = x;
        this.y = y;
        this.z = z;
    }

    /// <summary>
    /// A value whose components are all zero.
    /// </summary>
    public static readonly Vector3Int ZERO = new(0, 0, 0);
    /// <summary>
    /// A value whose components are all one.
    /// </summary>
    public static readonly Vector3Int ONE  = new(1, 1, 1);
    /// <summary>
    /// The up value used as part of this type's public representation.
    /// </summary>
    public static readonly Vector3Int UP = new(0, 1, 0);
    /// <summary>
    /// The down value used as part of this type's public representation.
    /// </summary>
    public static readonly Vector3Int DOWN = new(0, -1, 0);
    /// <summary>
    /// The left value used as part of this type's public representation.
    /// </summary>
    public static readonly Vector3Int LEFT = new(-1, 0, 0);
    /// <summary>
    /// The right value used as part of this type's public representation.
    /// </summary>
    public static readonly Vector3Int RIGHT = new(1, 0, 0);
    /// <summary>
    /// The forward value used as part of this type's public representation.
    /// </summary>
    public static readonly Vector3Int FORWARD = new(0, 0, 1);
    /// <summary>
    /// The back value used as part of this type's public representation.
    /// </summary>
    public static readonly Vector3Int BACK = new(0, 0, -1);

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
    /// The validated vector3int that represents the completed operation.
    /// </returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector3Int operator +(Vector3Int a, Vector3Int b)
        => new(a.x + b.x, a.y + b.y, a.z + b.z);

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
    /// The validated vector3int that represents the completed operation.
    /// </returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector3Int operator -(Vector3Int a, Vector3Int b)
        => new(a.x - b.x, a.y - b.y, a.z - b.z);

    /// <summary>
    /// Subtracts or negates the supplied value component by component.
    /// </summary>
    /// <param name="v">
    /// The concrete value read or transformed by this operation.
    /// </param>
    /// <returns>
    /// The validated vector3int that represents the completed operation.
    /// </returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector3Int operator -(Vector3Int v)
        => new(-v.x, -v.y, -v.z);

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
    /// The validated vector3int that represents the completed operation.
    /// </returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector3Int operator *(Vector3Int v, int scalar)
        => new(v.x * scalar, v.y * scalar, v.z * scalar);

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
    /// The validated vector3int that represents the completed operation.
    /// </returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector3Int operator *(int scalar, Vector3Int v)
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
    /// The validated vector3int that represents the completed operation.
    /// </returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector3Int operator /(Vector3Int v, int scalar)
        => new(v.x / scalar, v.y / scalar, v.z / scalar);

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
    public static bool operator ==(Vector3Int a, Vector3Int b)
        => a.x == b.x && a.y == b.y && a.z == b.z;

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
    public static bool operator !=(Vector3Int a, Vector3Int b)
        => !(a == b);

    /// <summary>
    /// Converts the supplied value to <see cref="Vector3"/>.
    /// </summary>
    /// <param name="v">
    /// The concrete value read or transformed by this operation.
    /// </param>
    /// <returns>
    /// The validated vector3 that represents the completed operation.
    /// </returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static explicit operator Vector3(Vector3Int v)
        => new(v.x, v.y, v.z);

    /// <summary>
    /// Converts the supplied value to <see cref="Vector3Int"/>.
    /// </summary>
    /// <param name="v">
    /// The concrete value read or transformed by this operation.
    /// </param>
    /// <returns>
    /// The validated vector3int that represents the completed operation.
    /// </returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static explicit operator Vector3Int(Vector3 v)
        => new((int)v.x, (int)v.y, (int)v.z);

    /// <summary>
    /// Determines whether this value and the supplied value represent the same logical state.
    /// </summary>
    /// <param name="obj">
    /// The object compared with this value.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the requested condition is satisfied; otherwise, <see langword="false"/>.
    /// </returns>
    public override bool Equals(object? obj)
        => obj is Vector3Int other && Equals(other);

    /// <summary>
    /// Determines whether this value and the supplied value represent the same logical state.
    /// </summary>
    /// <param name="other">
    /// The strongly typed value compared with this value.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the requested condition is satisfied; otherwise, <see langword="false"/>.
    /// </returns>
    public bool Equals(Vector3Int other)
        => x == other.x && y == other.y && z == other.z;

    /// <summary>
    /// Computes a hash code consistent with the implemented equality contract.
    /// </summary>
    /// <returns>
    /// The scalar result calculated from the supplied inputs.
    /// </returns>
    public override int GetHashCode()
        => HashCode.Combine(x, y, z);

    /// <summary>
    /// Formats this value as a human-readable component list.
    /// </summary>
    /// <returns>
    /// The validated text representation owned by the caller.
    /// </returns>
    public override string ToString()
        => $"({x}, {y}, {z})";
}
