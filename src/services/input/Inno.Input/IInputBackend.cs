using System;

namespace Inno.Input;

/// <summary>
/// Captures complete input state from one platform backend at frame boundaries.
/// </summary>
public interface IInputBackend : IDisposable
{
    /// <summary>
    /// Captures a snapshot and consumes transient press, release, movement, and wheel state.
    /// </summary>
    /// <param name="frameIndex">
    /// The runtime frame receiving the snapshot.
    /// </param>
    /// <returns>
    /// The immutable state for that exact frame.
    /// </returns>
    InputSnapshot Capture(long frameIndex);
}
