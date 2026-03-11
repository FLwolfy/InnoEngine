using System;
using System.Runtime.Serialization;

namespace Inno.Core.Math;

[DataContract]
public struct Vector2Int : IEquatable<Vector2Int>
{
    [DataMember] public int x;
    [DataMember] public int y;

    public Vector2Int(int x, int y)
    {
        this.x = x;
        this.y = y;
    }

    // Common constants
    public static readonly Vector2Int ZERO = new(0, 0);
    public static readonly Vector2Int ONE  = new(1, 1);
    public static readonly Vector2Int UNIT_X  = new(1, 0);
    public static readonly Vector2Int UNIT_Y  = new(0, 1);

    // Operators
    public static Vector2Int operator +(Vector2Int a, Vector2Int b)
        => new(a.x + b.x, a.y + b.y);

    public static Vector2Int operator -(Vector2Int a, Vector2Int b)
        => new(a.x - b.x, a.y - b.y);

    public static Vector2Int operator -(Vector2Int v)
        => new(-v.x, -v.y);

    public static Vector2Int operator *(Vector2Int v, int scalar)
        => new(v.x * scalar, v.y * scalar);

    public static Vector2Int operator *(int scalar, Vector2Int v)
        => v * scalar;

    public static Vector2Int operator /(Vector2Int v, int scalar)
        => new(v.x / scalar, v.y / scalar);

    public static bool operator ==(Vector2Int a, Vector2Int b)
        => a.x == b.x && a.y == b.y;

    public static bool operator !=(Vector2Int a, Vector2Int b)
        => !(a == b);

    // Conversions
    public static explicit operator Vector2(Vector2Int v)
        => new(v.x, v.y);

    public static explicit operator Vector2Int(Vector2 v)
        => new((int)v.x, (int)v.y);

    // Equality
    public override bool Equals(object? obj)
        => obj is Vector2Int other && Equals(other);

    public bool Equals(Vector2Int other)
        => x == other.x && y == other.y;

    public override int GetHashCode()
        => HashCode.Combine(x, y);

    public override string ToString()
        => $"({x}, {y})";
}