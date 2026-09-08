using System;

namespace Inno.Core.Identity;

/// <summary>
/// Packs and unpacks runtime ids into a single <see cref="int"/> using slot + generation.
/// </summary>
internal static class RuntimeIdCodec
{
    /// <summary>
    /// The slot bits value used as part of this type's public representation.
    /// </summary>
    public const int SLOT_BITS = 20;
    /// <summary>
    /// The generation bits value used as part of this type's public representation.
    /// </summary>
    public const int GENERATION_BITS = 12;

    /// <summary>
    /// The slot mask value used as part of this type's public representation.
    /// </summary>
    public const int SLOT_MASK = (1 << SLOT_BITS) - 1;
    /// <summary>
    /// The generation mask value used as part of this type's public representation.
    /// </summary>
    public const int GENERATION_MASK = (1 << GENERATION_BITS) - 1;

    /// <summary>
    /// Packs a storage slot and generation into one opaque runtime identifier.
    /// </summary>
    /// <param name="slot">
    /// The dense storage slot encoded in this runtime handle.
    /// </param>
    /// <param name="generation">
    /// The owner generation used to reject stale handles or snapshots.
    /// </param>
    /// <returns>
    /// The scalar result calculated from the supplied inputs.
    /// </returns>
    public static int Pack(int slot, int generation)
    {
        if ((uint)slot > SLOT_MASK)
            throw new ArgumentOutOfRangeException(nameof(slot));
        if ((uint)generation > GENERATION_MASK)
            throw new ArgumentOutOfRangeException(nameof(generation));

        return (generation << SLOT_BITS) | (slot & SLOT_MASK);
    }

    /// <summary>
    /// Extracts the storage slot encoded in an opaque runtime identifier.
    /// </summary>
    /// <param name="runtimeId">
    /// The runtime id consumed by unpack slot; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <returns>
    /// The scalar result calculated from the supplied inputs.
    /// </returns>
    public static int UnpackSlot(int runtimeId)
        => runtimeId & SLOT_MASK;

    /// <summary>
    /// Extracts the owner generation encoded in an opaque runtime identifier.
    /// </summary>
    /// <param name="runtimeId">
    /// The runtime id consumed by unpack generation; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <returns>
    /// The scalar result calculated from the supplied inputs.
    /// </returns>
    public static int UnpackGeneration(int runtimeId)
        => (runtimeId >> SLOT_BITS) & GENERATION_MASK;
}
