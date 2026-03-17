using System;
using System.Drawing;

namespace Inno.Core.Mathematics;

/// <summary>
/// Represents an axis-aligned rectangle with integer coordinates.
/// Coordinates assume Y-axis points upwards (top smaller than bottom).
/// </summary>
public struct Rect : IEquatable<Rect>
{
    public int x, y, width, height;

    public int left => x;
    public int right => x + width;
    public int top => y;
    public int bottom => y + height;

    public Rect(int x, int y, int width, int height)
    {
        this.x = x;
        this.y = y;
        this.width = width;
        this.height = height;
    }

    /// <summary>
    /// Checks if this rectangle overlaps another rectangle.
    /// </summary>
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
    public bool Contains(int px, int py)
    {
        return px >= left && px < right &&
               py >= top && py < bottom;
    }


    public static bool operator ==(Rect a, Rect b) => a.Equals(b);
    public static bool operator !=(Rect a, Rect b) => !a.Equals(b);

    public static Rect operator +(Rect a, Rect b) => new Rect(a.x + b.x, a.y + b.y, a.width + b.width, a.height + b.height);

    public static Rect operator -(Rect a, Rect b) => new Rect(a.x - b.x, a.y - b.y, a.width - b.width, a.height - b.height);

    public static implicit operator Rectangle(Rect r) => new Rectangle(r.x, r.y, r.width, r.height);

    public static implicit operator Rect(Rectangle r) => new Rect(r.X, r.Y, r.Width, r.Height);

    public static implicit operator System.Numerics.Vector4(Rect r) => new System.Numerics.Vector4(r.x, r.y, r.width, r.height);
    public static implicit operator Rect(System.Numerics.Vector4 v) => new Rect((int)v.X, (int)v.Y, (int)v.Z, (int)v.W);
    
    public override bool Equals(object? obj) => obj is Rect r && Equals(r);
    public bool Equals(Rect other) => x == other.x && y == other.y && width == other.width && height == other.height;
    public override int GetHashCode() => HashCode.Combine(x, y, width, height);
    public override string ToString() => $"({x}, {y}, {width}, {height})";
}
