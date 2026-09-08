namespace Inno.Native.Sdl3
{
    using System;

    /// <summary>
    /// Represents an integer native point exchanged with the SDL3 ABI.
    /// </summary>
public struct Point32 : IEquatable<Point32>
    {
        /// <summary>
        /// The x value used as part of this type's public representation.
        /// </summary>
public int X;
        /// <summary>
        /// The y value used as part of this type's public representation.
        /// </summary>
public int Y;

        /// <summary>
        /// Creates a validated point32 instance.
        /// </summary>
        /// <param name="x">
        /// The horizontal or first component.
        /// </param>
        /// <param name="y">
        /// The vertical or second component.
        /// </param>
public Point32(int x, int y)
        {
            X = x;
            Y = y;
        }

        /// <summary>
        /// Determines whether this value and the supplied value represent the same logical state.
        /// </summary>
        /// <param name="obj">
        /// The object compared with this value.
        /// </param>
        /// <returns>
        /// <see langword="true"/> when the documented condition is satisfied; otherwise, <see langword="false"/>.
        /// </returns>
public override readonly bool Equals(object? obj)
        {
            return obj is Point32 point && Equals(point);
        }

        /// <summary>
        /// Determines whether this value and the supplied value represent the same logical state.
        /// </summary>
        /// <param name="other">
        /// The strongly typed value compared with this value.
        /// </param>
        /// <returns>
        /// <see langword="true"/> when the documented condition is satisfied; otherwise, <see langword="false"/>.
        /// </returns>
public readonly bool Equals(Point32 other)
        {
            return X == other.X &&
                   Y == other.Y;
        }

        /// <summary>
        /// Computes a hash code consistent with the implemented equality contract.
        /// </summary>
        /// <returns>
        /// The scalar result calculated from the supplied inputs.
        /// </returns>
public override readonly int GetHashCode()
        {
            return HashCode.Combine(X, Y);
        }

        /// <summary>
        /// Determines whether the supplied values are equal under the type's equality tolerance.
        /// </summary>
        /// <param name="left">
        /// The left consumed by ==; ownership remains with the caller unless explicitly stated otherwise.
        /// </param>
        /// <param name="right">
        /// The right consumed by ==; ownership remains with the caller unless explicitly stated otherwise.
        /// </param>
        /// <returns>
        /// <see langword="true"/> when the documented condition is satisfied; otherwise, <see langword="false"/>.
        /// </returns>
public static bool operator ==(Point32 left, Point32 right)
        {
            return left.Equals(right);
        }

        /// <summary>
        /// Determines whether the supplied values differ under the type's equality tolerance.
        /// </summary>
        /// <param name="left">
        /// The left consumed by !=; ownership remains with the caller unless explicitly stated otherwise.
        /// </param>
        /// <param name="right">
        /// The right consumed by !=; ownership remains with the caller unless explicitly stated otherwise.
        /// </param>
        /// <returns>
        /// <see langword="true"/> when the documented condition is satisfied; otherwise, <see langword="false"/>.
        /// </returns>
public static bool operator !=(Point32 left, Point32 right)
        {
            return !(left == right);
        }
    }
}