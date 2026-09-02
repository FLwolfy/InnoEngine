using System;

namespace Inno.Rendering.Bgfx;

/// <summary>
/// Serializes ownership of BGFX's process-global native runtime without exposing process state through the device API.
/// </summary>
internal sealed class BgfxProcessDeviceLease : IDisposable
{
    private static readonly object S_SYNC = new();
    private static BgfxProcessDeviceLease? s_active;
    private static bool s_singleThreadedRuntimeConsumed;

    private readonly bool m_singleThreaded;
    private bool m_disposed;

    private BgfxProcessDeviceLease(bool singleThreaded)
    {
        m_singleThreaded = singleThreaded;
    }

    internal static BgfxProcessDeviceLease Acquire(bool singleThreaded)
    {
        lock (S_SYNC)
        {
            if (s_active is not null)
                throw new InvalidOperationException("Only one BGFX device may own the native process runtime.");
            if (singleThreaded && s_singleThreadedRuntimeConsumed)
            {
                throw new InvalidOperationException(
                    "BGFX single-threaded mode cannot be initialized again in the same process.");
            }

            var lease = new BgfxProcessDeviceLease(singleThreaded);
            s_active = lease;
            return lease;
        }
    }

    internal void MarkInitialized()
    {
        lock (S_SYNC)
        {
            if (!ReferenceEquals(s_active, this) || m_disposed)
                throw new InvalidOperationException("The BGFX process lease is not active.");
            if (m_singleThreaded)
                s_singleThreadedRuntimeConsumed = true;
        }
    }

    /// <summary>
    /// Releases the process runtime for a later device after native shutdown has completed.
    /// </summary>
    public void Dispose()
    {
        lock (S_SYNC)
        {
            if (m_disposed)
                return;
            if (!ReferenceEquals(s_active, this))
                throw new InvalidOperationException("BGFX process leases must be released by their owner.");
            m_disposed = true;
            s_active = null;
        }
    }
}
