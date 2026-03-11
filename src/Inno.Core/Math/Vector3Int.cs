using System;
using System.Runtime.Serialization;

namespace Inno.Core.Math;

[DataContract]
public struct Vector3Int : IEquatable<Vector3Int>
{
    [DataMember] public int x;
    [DataMember] public int y;
    [DataMember] public int z;

    public Vector3Int(int x, int y, int z)
    {
        this.x = x;
        this.y = y;
        this.z = z;
    }

    public static readonly Vector3Int ZERO = new(0, 0, 0);
    public static readonly Vector3Int ONE  = new(1, 1, 1);
    public static readonly Vector3Int UP = new(0, 1, 0);
    public static readonly Vector3Int DOWN = new(0, -1, 0);
    public static readonly Vector3Int LEFT = new(-1, 0, 0);
    public static readonly Vector3Int RIGHT = new(1, 0, 0);
    public static readonly Vector3Int FORWARD = new(0, 0, 1);
    public static readonly Vector3Int BACK = new(0, 0, -1);

    public static Vector3Int operator +(Vector3Int a, Vector3Int b)
        => new(a.x + b.x, a.y + b.y, a.z + b.z);

    public static Vector3Int operator -(Vector3Int a, Vector3Int b)
        => new(a.x - b.x, a.y - b.y, a.z - b.z);

    public static Vector3Int operator -(Vector3Int v)
        => new(-v.x, -v.y, -v.z);

    public static Vector3Int operator *(Vector3Int v, int scalar)
        => new(v.x * scalar, v.y * scalar, v.z * scalar);

    public static Vector3Int operator *(int scalar, Vector3Int v)
        => v * scalar;

    public static Vector3Int operator /(Vector3Int v, int scalar)
        => new(v.x / scalar, v.y / scalar, v.z / scalar);

    public static bool operator ==(Vector3Int a, Vector3Int b)
        => a.x == b.x && a.y == b.y && a.z == b.z;

    public static bool operator !=(Vector3Int a, Vector3Int b)
        => !(a == b);

    public static explicit operator Vector3(Vector3Int v)
        => new(v.x, v.y, v.z);

    public static explicit operator Vector3Int(Vector3 v)
        => new((int)v.x, (int)v.y, (int)v.z);

    public override bool Equals(object? obj)
        => obj is Vector3Int other && Equals(other);

    public bool Equals(Vector3Int other)
        => x == other.x && y == other.y && z == other.z;

    public override int GetHashCode()
        => HashCode.Combine(x, y, z);

    public override string ToString()
        => $"({x}, {y}, {z})";
}