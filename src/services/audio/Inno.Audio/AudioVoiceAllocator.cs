using System;
using System.Threading;

namespace Inno.Audio;

/// <summary>
/// Allocates service-owned playback handles without impersonating a backend audio device.
/// </summary>
public sealed class AudioVoiceAllocator
{
    private static int S_NEXT_GENERATION;
    private readonly uint m_generation;
    private ulong m_nextIdentity;

    /// <summary>
    /// Creates an isolated playback identity owner.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The process exhausted its owner generations.
    /// </exception>
    public AudioVoiceAllocator()
    {
        int generation = Interlocked.Increment(ref S_NEXT_GENERATION);
        if (generation <= 0)
            throw new InvalidOperationException("Audio playback owner generations are exhausted.");
        m_generation = (uint)generation;
    }

    /// <summary>
    /// Gets the process-unique owner generation encoded in every allocated playback handle.
    /// </summary>
    public uint generation => m_generation;

    /// <summary>
    /// Allocates a unique service handle that cannot alias a different service's playback.
    /// </summary>
    /// <returns>
    /// A new opaque playback handle; identities are never reused within this owner.
    /// </returns>
    /// <exception cref="OverflowException">
    /// The owner exhausted its playback identities.
    /// </exception>
    public AudioVoiceHandle Allocate() => new(checked(++m_nextIdentity), m_generation);
}
