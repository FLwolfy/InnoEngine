using System;
using System.Threading;

namespace Inno.Animation;

/// <summary>
/// Owns opaque playback identity encoding without requiring a runtime to inherit a fake service base.
/// </summary>
public sealed class AnimationPlaybackAllocator
{
    private static int S_NEXT_GENERATION;

    /// <summary>
    /// Creates a process-unique playback owner.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The process exhausted its playback owner identities.
    /// </exception>
    public AnimationPlaybackAllocator()
    {
        int next = Interlocked.Increment(ref S_NEXT_GENERATION);
        if (next <= 0)
            throw new InvalidOperationException("Animation owner generations are exhausted.");
        generation = (uint)next;
    }

    /// <summary>
    /// Gets the generation shared by handles allocated by this owner.
    /// </summary>
    public uint generation { get; }

    /// <summary>
    /// Encodes one owner-managed slot revision into an opaque playback handle.
    /// </summary>
    /// <param name="slot">
    /// The non-negative slot owned by the runtime.
    /// </param>
    /// <param name="slotGeneration">
    /// The non-zero revision used to reject reused slots.
    /// </param>
    /// <returns>
    /// A playback handle that cannot alias a different allocator.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The slot or its generation is invalid.
    /// </exception>
    public AnimationPlaybackHandle Create(int slot, uint slotGeneration)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(slot);
        if (slotGeneration == 0)
            throw new ArgumentOutOfRangeException(nameof(slotGeneration));
        return new AnimationPlaybackHandle(((ulong)slotGeneration << 32) | ((uint)slot + 1U), generation);
    }

    /// <summary>
    /// Decodes only handles issued by this owner; slot liveness remains the runtime's responsibility.
    /// </summary>
    /// <param name="handle">
    /// The opaque playback handle.
    /// </param>
    /// <returns>
    /// The slot and revision, or an invalid slot when the owner does not match.
    /// </returns>
    public (int slot, uint generation) Decode(AnimationPlaybackHandle handle)
    {
        if (!handle.isValid || handle.runtimeGeneration != generation)
            return (-1, 0);
        uint encodedSlot = (uint)(handle.value & uint.MaxValue);
        return (checked((int)(encodedSlot - 1U)), (uint)(handle.value >> 32));
    }
}
