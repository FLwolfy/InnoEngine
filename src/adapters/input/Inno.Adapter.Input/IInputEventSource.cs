using System;
using Inno.Core.Events;
using Inno.Input;

namespace Inno.Adapter.Input;

/// <summary>
/// Routes backend-neutral platform events into isolated runtime input backends.
/// </summary>
public interface IInputEventSource : IDisposable
{
    /// <summary>
    /// Creates an isolated input backend owned by one runtime session.
    /// </summary>
    /// <returns>
    /// A caller-owned input backend connected to this event source.
    /// </returns>
    IInputBackend CreateBackend();

    /// <summary>
    /// Routes one backend-neutral platform event to active input backends.
    /// </summary>
    /// <param name="evnt">
    /// Event produced by the active platform application.
    /// </param>
    void ProcessEvent(Event evnt);
}
