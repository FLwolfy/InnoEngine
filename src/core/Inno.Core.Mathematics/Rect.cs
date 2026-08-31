using System;
using System.Runtime.CompilerServices;

namespace Inno.Core.Mathematics;

/// <summary>
/// Represents an axis-aligned rectangle with float coordinates.
/// Coordinates assume Y-axis points upwards (top smaller than bottom).
/// </summary>
public struct Rect : IEquatable<Rect>
{
    public float x, y, width, height;

    public float left => x;
    public float right => x + width;
    public float top => y;
    public float bottom => y + height;

    public Vector2 min => new(x, y);
    public Vector2 max => new(x + width, y + height);
    public Vector2 size => new(width, height);
    public Vector2 center => new(x + width * 0.5f, y + height * 0.5f);

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
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Contains(float px, float py)
    {
        return px >= left && px < right &&
               py >= top && py < bottom;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Contains(Vector2 p) => Contains(p.x, p.y);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Rect FromMinMax(Vector2 min, Vector2 max)
    {
        return new Rect(min.x, min.y, max.x - min.x, max.y - min.y);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Rect Union(Rect a, Rect b)
    {
        float minX = MathF.Min(a.left, b.left);
        float minY = MathF.Min(a.top, b.top);
        float maxX = MathF.Max(a.right, b.right);
        float maxY = MathF.Max(a.bottom, b.bottom);
        return new Rect(minX, minY, maxX - minX, maxY - minY);
    }

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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator ==(Rect a, Rect b) => a.Equals(b);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator !=(Rect a, Rect b) => !a.Equals(b);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Rect operator +(Rect a, Rect b) => new Rect(a.x + b.x, a.y + b.y, a.width + b.width, a.height + b.height);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Rect operator -(Rect a, Rect b) => new Rect(a.x - b.x, a.y - b.y, a.width - b.width, a.height - b.height);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator System.Numerics.Vector4(Rect r) => new System.Numerics.Vector4(r.x, r.y, r.width, r.height);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator Rect(System.Numerics.Vector4 v) => new Rect(v.X, v.Y, v.Z, v.W);
    
    public override bool Equals(object? obj) => obj is Rect r && Equals(r);
    public bool Equals(Rect other) =>
        MathHelper.AlmostEquals(x, other.x) &&
        MathHelper.AlmostEquals(y, other.y) &&
        MathHelper.AlmostEquals(width, other.width) &&
        MathHelper.AlmostEquals(height, other.height);
    public override int GetHashCode() => HashCode.Combine(x, y, width, height);
    public override string ToString() => $"({x}, {y}, {width}, {height})";
}
