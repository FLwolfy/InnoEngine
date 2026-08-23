using System;
using System.Collections.Generic;

namespace Inno.Engine.Scene.Layers;

/// <summary>
/// Stores a compact set of scene layers for rendering, physics, and query filtering.
/// </summary>
public readonly struct LayerMask : IEquatable<LayerMask>
{
    /// <summary>
    /// Gets a mask containing no layers.
    /// </summary>
    public static LayerMask none { get; } = new(0u);

    /// <summary>
    /// Gets a mask containing every supported layer.
    /// </summary>
    public static LayerMask everything { get; } = new(uint.MaxValue);

    /// <summary>
    /// Creates a mask from its raw thirty-two-bit representation.
    /// </summary>
    /// <param name="value">The raw mask bits.</param>
    public LayerMask(uint value)
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
    /// <param name="layers">The layers whose bits should be enabled.</param>
    /// <returns>A mask containing every supplied layer.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="layers"/> is <see langword="null"/>.
    /// </exception>
    public static LayerMask FromLayers(IEnumerable<Layer> layers)
    {
        ArgumentNullException.ThrowIfNull(layers);
        uint bits = 0u;
        foreach (Layer layer in layers)
            bits |= 1u << layer.index;
        return new LayerMask(bits);
    }

    /// <summary>
    /// Determines whether the supplied layer is contained in this mask.
    /// </summary>
    /// <param name="layer">The layer to test.</param>
    /// <returns><see langword="true"/> when the layer bit is enabled.</returns>
    public bool Contains(Layer layer) => (value & (1u << layer.index)) != 0u;

    /// <summary>
    /// Returns a mask with the supplied layer enabled.
    /// </summary>
    /// <param name="layer">The layer to enable.</param>
    /// <returns>The updated immutable mask.</returns>
    public LayerMask With(Layer layer) => new(value | (1u << layer.index));

    /// <summary>
    /// Returns a mask with the supplied layer disabled.
    /// </summary>
    /// <param name="layer">The layer to disable.</param>
    /// <returns>The updated immutable mask.</returns>
    public LayerMask Without(Layer layer) => new(value & ~(1u << layer.index));

    /// <summary>
    /// Determines whether another mask contains the same layer bits.
    /// </summary>
    /// <param name="other">The mask to compare with this value.</param>
    /// <returns><see langword="true"/> when both masks contain the same bits.</returns>
    public bool Equals(LayerMask other) => value == other.value;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is LayerMask other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => value.GetHashCode();

    /// <inheritdoc />
    public override string ToString() => $"0x{value:X8}";

    /// <summary>
    /// Combines the enabled bits from two masks.
    /// </summary>
    /// <param name="left">The first mask.</param>
    /// <param name="right">The second mask.</param>
    /// <returns>The bitwise union of both masks.</returns>
    public static LayerMask operator |(LayerMask left, LayerMask right) => new(left.value | right.value);

    /// <summary>
    /// Retains only layer bits enabled in both masks.
    /// </summary>
    /// <param name="left">The first mask.</param>
    /// <param name="right">The second mask.</param>
    /// <returns>The bitwise intersection of both masks.</returns>
    public static LayerMask operator &(LayerMask left, LayerMask right) => new(left.value & right.value);

    /// <summary>
    /// Inverts every layer bit in a mask.
    /// </summary>
    /// <param name="mask">The mask to invert.</param>
    /// <returns>The complement of the supplied mask.</returns>
    public static LayerMask operator ~(LayerMask mask) => new(~mask.value);

    /// <summary>
    /// Determines whether two masks contain the same bits.
    /// </summary>
    /// <param name="left">The first mask.</param>
    /// <param name="right">The second mask.</param>
    /// <returns><see langword="true"/> when both masks are equal.</returns>
    public static bool operator ==(LayerMask left, LayerMask right) => left.Equals(right);

    /// <summary>
    /// Determines whether two masks contain different bits.
    /// </summary>
    /// <param name="left">The first mask.</param>
    /// <param name="right">The second mask.</param>
    /// <returns><see langword="true"/> when both masks differ.</returns>
    public static bool operator !=(LayerMask left, LayerMask right) => !left.Equals(right);
}
