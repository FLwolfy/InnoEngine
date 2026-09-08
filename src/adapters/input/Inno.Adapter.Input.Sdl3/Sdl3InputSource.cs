using System;
using System.Collections.Generic;

using Inno.Adapter.Input;
using Inno.Core.Events;
using Inno.Input;

namespace Inno.Adapter.Input.Sdl3;

/// <summary>
/// Broadcasts translated SDL events to isolated input backends owned by runtime sessions.
/// </summary>
public sealed class Sdl3InputSource : IInputEventSource
{
    private readonly List<Sdl3InputBackend> m_backends = [];
    private readonly uint m_windowId;
    private bool m_disposed;

    /// <summary>
    /// Creates an input source restricted to one window, or to all windows when the identifier is zero.
    /// </summary>
    /// <param name="windowId">
    /// The platform window identifier accepted by this source; zero accepts events from every window.
    /// </param>
    public Sdl3InputSource(uint windowId)
    {
        m_windowId = windowId;
    }

    /// <summary>
    /// Creates one isolated backend that receives subsequent events from this source.
    /// </summary>
    /// <returns>
    /// A caller-owned backend whose disposal unregisters it from this source.
    /// </returns>
    /// <exception cref="ObjectDisposedException">
    /// Thrown after this source has been disposed.
    /// </exception>
    public IInputBackend CreateBackend()
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);
        var backend = new Sdl3InputBackend(m_windowId, RemoveBackend);
        m_backends.Add(backend);
        return backend;
    }

    /// <summary>
    /// Broadcasts one translated platform event to every active runtime-session backend.
    /// </summary>
    /// <param name="evnt">
    /// The backend-neutral event produced by the SDL platform adapter.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="evnt"/> is null.
    /// </exception>
    /// <exception cref="ObjectDisposedException">
    /// Thrown after this source has been disposed.
    /// </exception>
    public void ProcessEvent(Event evnt)
    {
        ArgumentNullException.ThrowIfNull(evnt);
        ObjectDisposedException.ThrowIf(m_disposed, this);
        Sdl3InputBackend[] snapshot = m_backends.ToArray();
        foreach (Sdl3InputBackend backend in snapshot)
            backend.ProcessEvent(evnt);
    }

    /// <summary>
    /// Disconnects all session backends and rejects subsequent event delivery.
    /// </summary>
    public void Dispose()
    {
        if (m_disposed)
            return;
        m_disposed = true;
        Sdl3InputBackend[] snapshot = m_backends.ToArray();
        m_backends.Clear();
        foreach (Sdl3InputBackend backend in snapshot)
            backend.DisconnectSource();
    }

    private void RemoveBackend(Sdl3InputBackend backend)
        => m_backends.Remove(backend);
}
