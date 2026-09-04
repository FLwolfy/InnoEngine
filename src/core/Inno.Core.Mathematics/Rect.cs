using System;
using System.Runtime.CompilerServices;

namespace Inno.Core.Mathematics;

/// <summary>
/// Represents an axis-aligned rectangle with float coordinates.
/// Coordinates assume Y-axis points upwards (top smaller than bottom).
/// </summary>
public struct Rect : IEquatable<Rect>
{
    /// <summary>
    /// The horizontal or first component.
    /// </summary>
    public float x, y, width, height;

    /// <summary>
    /// Gets the scalar measurement or identity associated with the current state.
    /// </summary>
    public float left => x;
    /// <summary>
    /// Gets the scalar measurement or identity associated with the current state.
    /// </summary>
    public float right => x + width;
    /// <summary>
    /// Gets the scalar measurement or identity associated with the current state.
    /// </summary>
    public float top => y;
    /// <summary>
    /// Gets the scalar measurement or identity associated with the current state.
    /// </summary>
    public float bottom => y + height;

    /// <summary>
    /// Gets the minimum corner of this axis-aligned rectangle.
    /// </summary>
    public Vector2 min => new(x, y);
    /// <summary>
    /// Gets the maximum corner of this axis-aligned rectangle.
    /// </summary>
    public Vector2 max => new(x + width, y + height);
    /// <summary>
    /// Gets the width and height derived from the rectangle bounds.
    /// </summary>
    public Vector2 size => new(width, height);
    /// <summary>
    /// Gets the midpoint derived from the rectangle bounds.
    /// </summary>
    public Vector2 center => new(x + width * 0.5f, y + height * 0.5f);

    /// <summary>
    /// Creates a validated rect instance.
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
    public Rect(float x, float y, float width, float height)
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
    public bool Overlaps(Rect other)
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
    public bool Contains(Rect other)
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
    public bool Contains(float px, float py)
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
    public bool Contains(Vector2 p) => Contains(p.x, p.y);

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
    /// The validated rect that represents the completed operation.
    /// </returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Rect FromMinMax(Vector2 min, Vector2 max)
    {
        return new Rect(min.x, min.y, max.x - min.x, max.y - min.y);
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
    /// The validated rect that represents the completed operation.
    /// </returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Rect Union(Rect a, Rect b)
    {
        float minX = MathF.Min(a.left, b.left);
        float minY = MathF.Min(a.top, b.top);
        float maxX = MathF.Max(a.right, b.right);
        float maxY = MathF.Max(a.bottom, b.bottom);
        return new Rect(minX, minY, maxX - minX, maxY - minY);
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
    public static bool TryIntersect(Rect a, Rect b, out Rect intersection)
    {
        float minX = MathF.Max(a.left, b.left);
        float minY = MathF.Max(a.top, b.top);
        float maxX = MathF.Min(a.right, b.right);
        float maxY = MathF.Min(a.bottom, b.bottom);

        if (maxX <= minX || maxY <= minY)
        {
            intersection = default;
            return false;
        }

        intersection = new Rect(minX, minY, maxX - minX, maxY - minY);
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
    public static bool operator ==(Rect a, Rect b) => a.Equals(b);
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
    public static bool operator !=(Rect a, Rect b) => !a.Equals(b);

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
    /// The validated rect that represents the completed operation.
    /// </returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Rect operator +(Rect a, Rect b) => new Rect(a.x + b.x, a.y + b.y, a.width + b.width, a.height + b.height);

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
    /// The validated rect that represents the completed operation.
    /// </returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Rect operator -(Rect a, Rect b) => new Rect(a.x - b.x, a.y - b.y, a.width - b.width, a.height - b.height);

    /// <summary>
    /// Converts the supplied value to <see cref="System.Numerics.Vector4"/>.
    /// </summary>
    /// <param name="r">
    /// The r consumed by convert; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <returns>
    /// The validated system.numerics.vector4 that represents the completed operation.
    /// </returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator System.Numerics.Vector4(Rect r) => new System.Numerics.Vector4(r.x, r.y, r.width, r.height);
    /// <summary>
    /// Converts the supplied value to <see cref="Rect"/>.
    /// </summary>
    /// <param name="v">
    /// The concrete value read or transformed by this operation.
    /// </param>
    /// <returns>
    /// The validated rect that represents the completed operation.
    /// </returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator Rect(System.Numerics.Vector4 v) => new Rect(v.X, v.Y, v.Z, v.W);
    
    /// <summary>
    /// Determines whether this value and the supplied value represent the same logical state.
    /// </summary>
    /// <param name="obj">
    /// The object compared with this value.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the requested condition is satisfied; otherwise, <see langword="false"/>.
    /// </returns>
    public override bool Equals(object? obj) => obj is Rect r && Equals(r);
    /// <summary>
    /// Determines whether this value and the supplied value represent the same logical state.
    /// </summary>
    /// <param name="other">
    /// The strongly typed value compared with this value.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the requested condition is satisfied; otherwise, <see langword="false"/>.
    /// </returns>
    public bool Equals(Rect other) =>
        MathHelper.AlmostEquals(x, other.x) &&
        MathHelper.AlmostEquals(y, other.y) &&
        MathHelper.AlmostEquals(width, other.width) &&
        MathHelper.AlmostEquals(height, other.height);
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
