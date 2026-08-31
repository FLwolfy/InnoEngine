using System;

namespace Inno.Core.Identity;

/// <summary>
/// Packs and unpacks runtime ids into a single <see cref="int"/> using slot + generation.
/// </summary>
internal static class RuntimeIdCodec
{
    public const int SLOT_BITS = 20;
    public const int GENERATION_BITS = 12;

    public const int SLOT_MASK = (1 << SLOT_BITS) - 1;
    public const int GENERATION_MASK = (1 << GENERATION_BITS) - 1;

    public static int Pack(int slot, int generation)
    {
        if ((uint)slot > SLOT_MASK)
            throw new ArgumentOutOfRangeException(nameof(slot));
        if ((uint)generation > GENERATION_MASK)
            throw new ArgumentOutOfRangeException(nameof(generation));

        return (generation << SLOT_BITS) | (slot & SLOT_MASK);
    }

    public static int UnpackSlot(int runtimeId)
        => runtimeId & SLOT_MASK;

    public static int UnpackGeneration(int runtimeId)
        => (runtimeId >> SLOT_BITS) & GENERATION_MASK;
}
