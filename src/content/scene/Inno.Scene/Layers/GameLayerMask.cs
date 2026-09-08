using System;
using System.Collections.Generic;

namespace Inno.Scene.Layers;

/// <summary>
/// Stores a compact set of scene layers for rendering, physics, and query filtering.
/// </summary>
public readonly struct GameLayerMask : IEquatable<GameLayerMask>
{
    /// <summary>
    /// Gets a mask containing no layers.
    /// </summary>
    public static GameLayerMask none { get; } = new(0u);

    /// <summary>
    /// Gets a mask containing every supported layer.
    /// </summary>
    public static GameLayerMask everything { get; } = new(uint.MaxValue);

    /// <summary>
    /// Creates a mask from its raw thirty-two-bit representation.
    /// </summary>
    /// <param name="value">
    /// The raw mask bits.
    /// </param>
    public GameLayerMask(uint value)
    {
        this.value = value;
    }

    /// <summary>
    /// Gets the raw thirty-two-bit mask value.
    /// </summary>
    public uint value { get; }

    /// <summary>
    /// Creates a mask containing the supplied layers.
    /// </summary>
    /// <param name="layers">
    /// The layers whose bits should be enabled.
    /// </param>
    /// <returns>
    /// A mask containing every supplied layer.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="layers"/> is <see langword="null"/>.
    /// </exception>
    public static GameLayerMask FromLayers(IEnumerable<GameLayer> layers)
    {
        ArgumentNullException.ThrowIfNull(layers);
        uint bits = 0u;
        foreach (GameLayer layer in layers)
            bits |= 1u << layer.index;
        return new GameLayerMask(bits);
    }

    /// <summary>
    /// Determines whether the supplied layer is contained in this mask.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> when the layer bit is enabled.
    /// </returns>
    /// <param name="layer">
    /// The layer consumed by contains; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    public bool Contains(GameLayer layer) => (value & (1u << layer.index)) != 0u;

    /// <summary>
    /// Returns a mask with the supplied layer enabled.
    /// </summary>
    /// <returns>
    /// The updated immutable mask.
    /// </returns>
    /// <param name="layer">
    /// The layer consumed by with; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    public GameLayerMask With(GameLayer layer) => new(value | (1u << layer.index));

    /// <summary>
    /// Returns a mask with the supplied layer disabled.
    /// </summary>
    /// <returns>
    /// The updated immutable mask.
    /// </returns>
    /// <param name="layer">
    /// The layer consumed by without; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    public GameLayerMask Without(GameLayer layer) => new(value & ~(1u << layer.index));

    /// <summary>
    /// Determines whether another mask contains the same layer bits.
    /// </summary>
    /// <param name="other">
    /// The mask to compare with this value.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when both masks contain the same bits.
    /// </returns>
    public bool Equals(GameLayerMask other) => value == other.value;

    /// <summary>
    /// Determines whether this instance and the supplied value represent the same logical state.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> when both values represent the same logical state; otherwise, <see langword="false"/>.
    /// </returns>
    /// <param name="obj">
    /// The object to compare with this instance.
    /// </param>
    public override bool Equals(object? obj) => obj is GameLayerMask other && Equals(other);

    /// <summary>
    /// Computes a hash code from the fields that participate in logical equality.
    /// </summary>
    /// <returns>
    /// A hash code consistent with the implemented equality contract.
    /// </returns>
    public override int GetHashCode() => value.GetHashCode();

    /// <summary>
    /// Formats this value as a human-readable representation.
    /// </summary>
    /// <returns>
    /// The human-readable representation of this value.
    /// </returns>
    public override string ToString() => $"0x{value:X8}";

    /// <summary>
    /// Combines the enabled bits from two masks.
    /// </summary>
    /// <param name="left">
    /// The first mask.
    /// </param>
    /// <param name="right">
    /// The second mask.
    /// </param>
    /// <returns>
    /// The bitwise union of both masks.
    /// </returns>
    public static GameLayerMask operator |(GameLayerMask left, GameLayerMask right) => new(left.value | right.value);

    /// <summary>
    /// Retains only layer bits enabled in both masks.
    /// </summary>
    /// <param name="left">
    /// The first mask.
    /// </param>
    /// <param name="right">
    /// The second mask.
    /// </param>
    /// <returns>
    /// The bitwise intersection of both masks.
    /// </returns>
    public static GameLayerMask operator &(GameLayerMask left, GameLayerMask right) => new(left.value & right.value);

    /// <summary>
    /// Inverts every layer bit in a mask.
    /// </summary>
    /// <param name="mask">
    /// The mask to invert.
    /// </param>
    /// <returns>
    /// The complement of the supplied mask.
    /// </returns>
    public static GameLayerMask operator ~(GameLayerMask mask) => new(~mask.value);

    /// <summary>
    /// Determines whether two masks contain the same bits.
    /// </summary>
    /// <param name="left">
    /// The first mask.
    /// </param>
    /// <param name="right">
    /// The second mask.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when both masks are equal.
    /// </returns>
    public static bool operator ==(GameLayerMask left, GameLayerMask right) => left.Equals(right);

    /// <summary>
    /// Determines whether two masks contain different bits.
    /// </summary>
    /// <param name="left">
    /// The first mask.
    /// </param>
    /// <param name="right">
    /// The second mask.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when both masks differ.
    /// </returns>
    public static bool operator !=(GameLayerMask left, GameLayerMask right) => !left.Equals(right);
}
