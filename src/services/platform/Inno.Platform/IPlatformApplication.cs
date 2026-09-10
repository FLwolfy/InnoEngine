using System;
using System.Collections.Generic;
using Inno.Core.Events;

namespace Inno.Platform;

/// <summary>
/// Defines the backend-neutral lifetime, window creation, and event-polling contract for a platform session.
/// </summary>
public interface IPlatformApplication : IDisposable
{
    /// <summary>
    /// Requests one complete host frame on the event-pump thread while the operating system
    /// owns a modal window resize loop. Handlers must not poll events or reenter an active frame.
    /// </summary>
    event Action<uint>? redrawRequested;

    /// <summary>
    /// Creates a window owned by this platform session.
    /// </summary>
    /// <param name="options">
    /// The validated initial window properties.
    /// </param>
    /// <returns>
    /// The newly created window. The caller may dispose it before disposing the application.
    /// </returns>
    /// <exception cref="ObjectDisposedException">
    /// Thrown when the platform application has already been disposed.
    /// </exception>
    IPlatformWindow CreateWindow(PlatformWindowOptions options);

    /// <summary>
    /// Attempts to dequeue the next backend-neutral platform event.
    /// </summary>
    /// <param name="evnt">
    /// The translated event when one is available; otherwise, <see langword="null"/>.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when an event was returned; otherwise, <see langword="false"/>.
    /// </returns>
    /// <exception cref="ObjectDisposedException">
    /// Thrown when the platform application has already been disposed.
    /// </exception>
    bool PollEvent(out Event? evnt);

    /// <summary>
    /// Captures the currently valid windows owned or tracked by this platform session.
    /// </summary>
    /// <returns>
    /// An immutable-by-convention snapshot of the current windows.
    /// </returns>
    /// <exception cref="ObjectDisposedException">
    /// Thrown when the platform application has already been disposed.
    /// </exception>
    IReadOnlyList<IPlatformWindow> GetWindows();
}
