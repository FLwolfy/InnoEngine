using System;
using System.Drawing;
using System.Runtime.CompilerServices;

namespace Inno.Core.Mathematics;

/// <summary>
/// Represents an axis-aligned rectangle with integer coordinates.
/// Coordinates assume Y-axis points upwards (top smaller than bottom).
/// </summary>
public struct RectInt : IEquatable<RectInt>
{
    /// <summary>
    /// The horizontal or first component.
    /// </summary>
    public int x, y, width, height;

    /// <summary>
    /// Gets the scalar measurement or identity associated with the current state.
    /// </summary>
    public int left => x;
    /// <summary>
    /// Gets the scalar measurement or identity associated with the current state.
    /// </summary>
    public int right => x + width;
    /// <summary>
    /// Gets the scalar measurement or identity associated with the current state.
    /// </summary>
    public int top => y;
    /// <summary>
    /// Gets the scalar measurement or identity associated with the current state.
    /// </summary>
    public int bottom => y + height;

    /// <summary>
    /// Gets the minimum corner of this axis-aligned rectangle.
    /// </summary>
    public Vector2Int min => new(x, y);
    /// <summary>
    /// Gets the maximum corner of this axis-aligned rectangle.
    /// </summary>
    public Vector2Int max => new(x + width, y + height);
    /// <summary>
    /// Gets the width and height derived from the rectangle bounds.
    /// </summary>
    public Vector2Int size => new(width, height);
    /// <summary>
    /// Gets the midpoint derived from the rectangle bounds.
    /// </summary>
    public Vector2Int center => new(x + width / 2, y + height / 2);

    /// <summary>
    /// Creates a validated rect int instance.
    /// </summary>
    /// <param name="x">
    /// The horizontal or first component.
    /// </param>
    /// <param name="y">
    /// The vertical or second component.
    /// </param>
    /// <param name="width">
    /// The width in logical units or pixels required by this operation.
    /// </param>
    /// <param name="height">
    /// The height in logical units or pixels required by this operation.
    /// </param>
    public RectInt(int x, int y, int width, int height)
    {
        this.x = x;
        this.y = y;
        this.width = width;
        this.height = height;
    }

    /// <summary>
    /// Checks if this rectangle overlaps another rectangle.
    /// </summary>
    /// <param name="other">
    /// The value to compare with this instance.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the operation succeeds or its condition is satisfied; otherwise, <see langword="false"/>.
    /// </returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Overlaps(RectInt other)
    {
        return !(right <= other.left ||
                 left >= other.right ||
                 bottom <= other.top ||
                 top >= other.bottom);
    }

    /// <summary>
    /// Checks if this rectangle fully contains another rectangle.
    /// </summary>
    /// <param name="other">
    /// The value to compare with this instance.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the operation succeeds or its condition is satisfied; otherwise, <see langword="false"/>.
    /// </returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Contains(RectInt other)
    {
        return left <= other.left &&
               top <= other.top &&
               right >= other.right &&
               bottom >= other.bottom;
    }

    /// <summary>
    /// Checks if this rectangle contains a point.
    /// </summary>
    /// <param name="px">
    /// The px consumed by contains; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <param name="py">
    /// The py consumed by contains; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the operation succeeds or its condition is satisfied; otherwise, <see langword="false"/>.
    /// </returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Contains(int px, int py)
    {
        return px >= left && px < right &&
               py >= top && py < bottom;
    }

    /// <summary>
    /// Determines whether current state contains the requested value value.
    /// </summary>
    /// <param name="p">
    /// The p consumed by contains; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the requested condition is satisfied; otherwise, <see langword="false"/>.
    /// </returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Contains(Vector2Int p) => Contains(p.x, p.y);

    /// <summary>
    /// Creates a rectangle from inclusive minimum and maximum corner values.
    /// </summary>
    /// <param name="min">
    /// The min consumed by from min max; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <param name="max">
    /// The max consumed by from min max; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <returns>
    /// The validated rect int that represents the completed operation.
    /// </returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static RectInt FromMinMax(Vector2Int min, Vector2Int max)
    {
        return new RectInt(min.x, min.y, max.x - min.x, max.y - min.y);
    }

    /// <summary>
    /// Returns the smallest axis-aligned rectangle containing both supplied rectangles.
    /// </summary>
    /// <param name="a">
    /// The first operand or interpolation endpoint.
    /// </param>
    /// <param name="b">
    /// The second operand or interpolation endpoint.
    /// </param>
    /// <returns>
    /// The validated rect int that represents the completed operation.
    /// </returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static RectInt Union(RectInt a, RectInt b)
    {
        int minX = Math.Min(a.left, b.left);
        int minY = Math.Min(a.top, b.top);
        int maxX = Math.Max(a.right, b.right);
        int maxY = Math.Max(a.bottom, b.bottom);
        return new RectInt(minX, minY, maxX - minX, maxY - minY);
    }

    /// <summary>
    /// Attempts to intersect without changing state when the operation cannot complete.
    /// </summary>
    /// <param name="a">
    /// The first operand or interpolation endpoint.
    /// </param>
    /// <param name="b">
    /// The second operand or interpolation endpoint.
    /// </param>
    /// <param name="intersection">
    /// The intersection consumed by try intersect; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the requested condition is satisfied; otherwise, <see langword="false"/>.
    /// </returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryIntersect(RectInt a, RectInt b, out RectInt intersection)
    {
        int minX = Math.Max(a.left, b.left);
        int minY = Math.Max(a.top, b.top);
        int maxX = Math.Min(a.right, b.right);
        int maxY = Math.Min(a.bottom, b.bottom);

        if (maxX <= minX || maxY <= minY)
        {
            intersection = default;
            return false;
        }

        intersection = new RectInt(minX, minY, maxX - minX, maxY - minY);
        return true;
    }

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
    public static bool operator ==(RectInt a, RectInt b) => a.Equals(b);
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
    public static bool operator !=(RectInt a, RectInt b) => !a.Equals(b);

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
    /// The validated rect int that represents the completed operation.
    /// </returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static RectInt operator +(RectInt a, RectInt b) => new RectInt(a.x + b.x, a.y + b.y, a.width + b.width, a.height + b.height);

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
    /// The validated rect int that represents the completed operation.
    /// </returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static RectInt operator -(RectInt a, RectInt b) => new RectInt(a.x - b.x, a.y - b.y, a.width - b.width, a.height - b.height);

    /// <summary>
    /// Converts the supplied value to <see cref="Rectangle"/>.
    /// </summary>
    /// <param name="r">
    /// The r consumed by convert; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <returns>
    /// The validated rectangle that represents the completed operation.
    /// </returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator Rectangle(RectInt r) => new Rectangle(r.x, r.y, r.width, r.height);

    /// <summary>
    /// Converts the supplied value to <see cref="RectInt"/>.
    /// </summary>
    /// <param name="r">
    /// The r consumed by convert; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <returns>
    /// The validated rect int that represents the completed operation.
    /// </returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator RectInt(Rectangle r) => new RectInt(r.X, r.Y, r.Width, r.Height);

    /// <summary>
    /// Converts the supplied value to <see cref="Vector4Int"/>.
    /// </summary>
    /// <param name="r">
    /// The r consumed by convert; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <returns>
    /// The validated vector4int that represents the completed operation.
    /// </returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator Vector4Int(RectInt r) => new Vector4Int(r.x, r.y, r.width, r.height);

    /// <summary>
    /// Converts the supplied value to <see cref="RectInt"/>.
    /// </summary>
    /// <param name="v">
    /// The concrete value read or transformed by this operation.
    /// </param>
    /// <returns>
    /// The validated rect int that represents the completed operation.
    /// </returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator RectInt(Vector4Int v) => new RectInt(v.x, v.y, v.z, v.w);

    /// <summary>
    /// Determines whether this value and the supplied value represent the same logical state.
    /// </summary>
    /// <param name="obj">
    /// The object compared with this value.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the requested condition is satisfied; otherwise, <see langword="false"/>.
    /// </returns>
    public override bool Equals(object? obj) => obj is RectInt r && Equals(r);
    /// <summary>
    /// Determines whether this value and the supplied value represent the same logical state.
    /// </summary>
    /// <param name="other">
    /// The strongly typed value compared with this value.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the requested condition is satisfied; otherwise, <see langword="false"/>.
    /// </returns>
    public bool Equals(RectInt other) => x == other.x && y == other.y && width == other.width && height == other.height;
    /// <summary>
    /// Computes a hash code consistent with the implemented equality contract.
    /// </summary>
    /// <returns>
    /// The scalar result calculated from the supplied inputs.
    /// </returns>
    public override int GetHashCode() => HashCode.Combine(x, y, width, height);
    /// <summary>
    /// Formats this value as a human-readable component list.
    /// </summary>
    /// <returns>
    /// The validated text representation owned by the caller.
    /// </returns>
    public override string ToString() => $"({x}, {y}, {width}, {height})";
}
