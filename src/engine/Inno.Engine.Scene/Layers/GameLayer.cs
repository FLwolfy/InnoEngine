using System;

namespace Inno.Engine.Scene.Layers;

/// <summary>
/// Identifies one of the thirty-two runtime layers available to scene objects.
/// </summary>
public readonly struct GameLayer : IEquatable<GameLayer>, IComparable<GameLayer>
{
    /// <summary>
    /// Defines the number of layer slots supported by the runtime bit-mask representation.
    /// </summary>
    public const int C_MAX_COUNT = 32;

    /// <summary>
    /// Gets the built-in default layer stored in slot zero.
    /// </summary>
    public static GameLayer defaultLayer { get; } = new(0);

    /// <summary>
    /// Creates a layer identifier from a zero-based layer index.
    /// </summary>
    /// <param name="index">The layer index in the inclusive range from zero through thirty-one.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="index"/> is outside the supported range.
    /// </exception>
    public GameLayer(int index)
    {
        if ((uint)index >= C_MAX_COUNT)
            throw new ArgumentOutOfRangeException(nameof(index), index, "A layer index must be between 0 and 31.");
        this.index = index;
    }

    /// <summary>
    /// Gets the zero-based layer index.
    /// </summary>
    public int index { get; }

    /// <summary>
    /// Compares this layer with another layer by index.
    /// </summary>
    /// <param name="other">The layer to compare with this value.</param>
    /// <returns>A signed value describing the relative index ordering.</returns>
    public int CompareTo(GameLayer other) => index.CompareTo(other.index);

    /// <summary>
    /// Determines whether another layer identifies the same index.
    /// </summary>
    /// <param name="other">The layer to compare with this value.</param>
    /// <returns><see langword="true"/> when both layers identify the same index.</returns>
    public bool Equals(GameLayer other) => index == other.index;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is GameLayer other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => index;

    /// <inheritdoc />
    public override string ToString() => index.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// Determines whether two layer identifiers contain the same index.
    /// </summary>
    /// <param name="left">The first layer identifier.</param>
    /// <param name="right">The second layer identifier.</param>
    /// <returns><see langword="true"/> when the identifiers are equal.</returns>
    public static bool operator ==(GameLayer left, GameLayer right) => left.Equals(right);

    /// <summary>
    /// Determines whether two layer identifiers contain different indices.
    /// </summary>
    /// <param name="left">The first layer identifier.</param>
    /// <param name="right">The second layer identifier.</param>
    /// <returns><see langword="true"/> when the identifiers are different.</returns>
    public static bool operator !=(GameLayer left, GameLayer right) => !left.Equals(right);
}
