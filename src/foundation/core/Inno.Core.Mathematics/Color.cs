using System;
using System.Runtime.CompilerServices;

namespace Inno.Core.Mathematics;

/// <summary>
/// Represents an RGBA color with float components in the range [0, 1].
/// </summary>
public struct Color : IEquatable<Color>
{
    /// <summary>
    /// The red color component.
    /// </summary>
    public float r;
    /// <summary>
    /// The green color component.
    /// </summary>
    public float g;
    /// <summary>
    /// The blue color component.
    /// </summary>
    public float b;
    /// <summary>
    /// The alpha color component.
    /// </summary>
    public float a;

    /// <summary>
    /// Creates a validated color instance.
    /// </summary>
    /// <param name="r">
    /// The r consumed by color; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <param name="g">
    /// The g consumed by color; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <param name="b">
    /// The second operand or interpolation endpoint.
    /// </param>
    /// <param name="a">
    /// The first operand or interpolation endpoint.
    /// </param>
    public Color(float r, float g, float b, float a = 1f)
    {
        this.r = Math.Clamp(r, 0.0f, 1.0f);
        this.g = Math.Clamp(g, 0.0f, 1.0f);
        this.b = Math.Clamp(b, 0.0f, 1.0f);
        this.a = Math.Clamp(a, 0.0f, 1.0f);
    }

    /// <summary>
    /// Creates a normalized color from byte channel values.
    /// </summary>
    /// <param name="r">
    /// The r consumed by from bytes; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <param name="g">
    /// The g consumed by from bytes; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <param name="b">
    /// The second operand or interpolation endpoint.
    /// </param>
    /// <param name="a">
    /// The first operand or interpolation endpoint.
    /// </param>
    /// <returns>
    /// The validated color that represents the completed operation.
    /// </returns>
    public static Color FromBytes(byte r, byte g, byte b, byte a = 255)
    {
        return new Color(r / 255f, g / 255f, b / 255f, a / 255f);
    }
    
    /// <summary>
    /// Converts normalized color channels to clamped byte values.
    /// </summary>
    /// <returns>
    /// The validated (byte r, byte g, byte b, byte a) that represents the completed operation.
    /// </returns>
    public (byte R, byte G, byte B, byte A) ToBytes()
    {
        return (
            (byte)(r * 255),
            (byte)(g * 255),
            (byte)(b * 255),
            (byte)(a * 255)
        );
    }
    
    /// <summary>
    /// Packs the color channels into an unsigned ARGB value.
    /// </summary>
    /// <returns>
    /// The validated uint that represents the completed operation.
    /// </returns>
    public uint ToUInt32ARGB()
    {
        byte red = (byte)(r * 255);
        byte green = (byte)(g * 255);
        byte blue = (byte)(b * 255);
        byte alpha = (byte)(a * 255);

        return (uint)((alpha << 24) | (blue << 16) | (green << 8) | red);
    }
    /// <summary>
    /// Formats this value as a human-readable component list.
    /// </summary>
    /// <returns>
    /// The validated text representation owned by the caller.
    /// </returns>

    
    public override string ToString() => $"Color({r:F2}, {g:F2}, {b:F2}, {a:F2})";
    
    /// <summary>
    /// The transparent value used as part of this type's public representation.
    /// </summary>
    public static readonly Color TRANSPARENT = new(0, 0, 0, 0);
    /// <summary>
    /// The white value used as part of this type's public representation.
    /// </summary>
    public static readonly Color WHITE = new(1, 1, 1, 1);
    /// <summary>
    /// The black value used as part of this type's public representation.
    /// </summary>
    public static readonly Color BLACK = new(0, 0, 0, 1);
    /// <summary>
    /// The red value used as part of this type's public representation.
    /// </summary>
    public static readonly Color RED = FromBytes(255, 0, 0);
    /// <summary>
    /// The green value used as part of this type's public representation.
    /// </summary>
    public static readonly Color GREEN = FromBytes(0, 255, 0);
    /// <summary>
    /// The blue value used as part of this type's public representation.
    /// </summary>
    public static readonly Color BLUE = FromBytes(0, 0, 255);
    /// <summary>
    /// The yellow value used as part of this type's public representation.
    /// </summary>
    public static readonly Color YELLOW = FromBytes(255, 255, 0);
    /// <summary>
    /// The magenta value used as part of this type's public representation.
    /// </summary>
    public static readonly Color MAGENTA = FromBytes(255, 0, 255);
    /// <summary>
    /// The cyan value used as part of this type's public representation.
    /// </summary>
    public static readonly Color CYAN = FromBytes(0, 255, 255);
    /// <summary>
    /// The gray value used as part of this type's public representation.
    /// </summary>
    public static readonly Color GRAY = FromBytes(128, 128, 128);
    /// <summary>
    /// The lightgray value used as part of this type's public representation.
    /// </summary>
    public static readonly Color LIGHTGRAY = FromBytes(211, 211, 211);
    /// <summary>
    /// The darkgray value used as part of this type's public representation.
    /// </summary>
    public static readonly Color DARKGRAY = FromBytes(64, 64, 64);
    /// <summary>
    /// The orange value used as part of this type's public representation.
    /// </summary>
    public static readonly Color ORANGE = FromBytes(255, 165, 0);
    /// <summary>
    /// The pink value used as part of this type's public representation.
    /// </summary>
    public static readonly Color PINK = FromBytes(255, 192, 203);
    /// <summary>
    /// The purple value used as part of this type's public representation.
    /// </summary>
    public static readonly Color PURPLE = FromBytes(128, 0, 128);
    /// <summary>
    /// The brown value used as part of this type's public representation.
    /// </summary>
    public static readonly Color BROWN = FromBytes(139, 69, 19);
    /// <summary>
    /// The cornflowerblue value used as part of this type's public representation.
    /// </summary>
    public static readonly Color CORNFLOWERBLUE = FromBytes(100, 149, 237);
    
    /// <summary>
    /// Multiplies the supplied values according to their algebraic contract.
    /// </summary>
    /// <param name="c">
    /// The c consumed by *; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <param name="factor">
    /// The factor consumed by *; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <returns>
    /// The validated color that represents the completed operation.
    /// </returns>
    public static Color operator *(Color c, float factor)
    {
        return new Color(
            c.r * factor,
            c.g * factor,
            c.b * factor,
            c.a * factor
        );
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
    public static bool operator ==(Color a, Color b)
    {
        return MathHelper.AlmostEquals(a.r, b.r) &&
               MathHelper.AlmostEquals(a.g, b.g) &&
               MathHelper.AlmostEquals(a.b, b.b) &&
               MathHelper.AlmostEquals(a.a, b.a);
    }

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
    public static bool operator !=(Color a, Color b) => !(a == b);

    /// <summary>
    /// Determines whether this value and the supplied value represent the same logical state.
    /// </summary>
    /// <param name="obj">
    /// The object compared with this value.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the requested condition is satisfied; otherwise, <see langword="false"/>.
    /// </returns>
    public override bool Equals(object? obj) => obj is Color other && Equals(other);

    /// <summary>
    /// Determines whether this value and the supplied value represent the same logical state.
    /// </summary>
    /// <param name="other">
    /// The strongly typed value compared with this value.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the requested condition is satisfied; otherwise, <see langword="false"/>.
    /// </returns>
    public bool Equals(Color other) => this == other;

    /// <summary>
    /// Computes a hash code consistent with the implemented equality contract.
    /// </summary>
    /// <returns>
    /// The scalar result calculated from the supplied inputs.
    /// </returns>
    public override int GetHashCode() => HashCode.Combine(r, g, b, a);
}
