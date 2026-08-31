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
    public int x, y, width, height;

    public int left => x;
    public int right => x + width;
    public int top => y;
    public int bottom => y + height;

    public Vector2Int min => new(x, y);
    public Vector2Int max => new(x + width, y + height);
    public Vector2Int size => new(width, height);
    public Vector2Int center => new(x + width / 2, y + height / 2);

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
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Contains(int px, int py)
    {
        return px >= left && px < right &&
               py >= top && py < bottom;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Contains(Vector2Int p) => Contains(p.x, p.y);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static RectInt FromMinMax(Vector2Int min, Vector2Int max)
    {
        return new RectInt(min.x, min.y, max.x - min.x, max.y - min.y);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static RectInt Union(RectInt a, RectInt b)
    {
        int minX = Math.Min(a.left, b.left);
        int minY = Math.Min(a.top, b.top);
        int maxX = Math.Max(a.right, b.right);
        int maxY = Math.Max(a.bottom, b.bottom);
        return new RectInt(minX, minY, maxX - minX, maxY - minY);
    }

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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator ==(RectInt a, RectInt b) => a.Equals(b);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator !=(RectInt a, RectInt b) => !a.Equals(b);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static RectInt operator +(RectInt a, RectInt b) => new RectInt(a.x + b.x, a.y + b.y, a.width + b.width, a.height + b.height);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static RectInt operator -(RectInt a, RectInt b) => new RectInt(a.x - b.x, a.y - b.y, a.width - b.width, a.height - b.height);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator Rectangle(RectInt r) => new Rectangle(r.x, r.y, r.width, r.height);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator RectInt(Rectangle r) => new RectInt(r.X, r.Y, r.Width, r.Height);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator Vector4Int(RectInt r) => new Vector4Int(r.x, r.y, r.width, r.height);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator RectInt(Vector4Int v) => new RectInt(v.x, v.y, v.z, v.w);

    public override bool Equals(object? obj) => obj is RectInt r && Equals(r);
    public bool Equals(RectInt other) => x == other.x && y == other.y && width == other.width && height == other.height;
    public override int GetHashCode() => HashCode.Combine(x, y, width, height);
    public override string ToString() => $"({x}, {y}, {width}, {height})";
}
