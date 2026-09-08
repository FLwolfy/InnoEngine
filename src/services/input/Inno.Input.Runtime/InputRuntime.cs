using Inno.Runtime.Contracts;
using System;

using Inno.Runtime;

namespace Inno.Input.Runtime;

/// <summary>
/// Captures one immutable input snapshot and binds it for the complete runtime frame.
/// </summary>
public sealed class InputRuntime : RuntimeSubsystem, IInputService
{
    private readonly IInputBackend m_backend;

    /// <summary>
    /// Creates a feature whose backend ownership transfers to this instance.
    /// </summary>
    /// <param name="backend">
    /// The platform backend captured at frame boundaries.
    /// </param>
    public InputRuntime(IInputBackend backend)
    {
        m_backend = backend ?? throw new ArgumentNullException(nameof(backend));
    }

    /// <summary>
    /// Gets the immutable input state captured at the beginning of the current frame.
    /// </summary>
    public InputSnapshot snapshot { get; private set; } = InputSnapshot.empty;

    /// <summary>
    /// Captures pending backend input and binds the resulting snapshot to the runtime frame.
    /// </summary>
    /// <param name="frame">
    /// The immutable frame state whose index labels the captured snapshot.
    /// </param>
    protected override void OnBeginFrame(RuntimeFrame frame)
    {
        snapshot = m_backend.Capture(frame.frameIndex);
        OwnFrameScope(InputExecutionContext.EnterScope(this));
    }
    /// <summary>
    /// Releases the storage backend after owned work has quiesced.
    /// </summary>
    protected override void OnStop() => m_backend.Dispose();
}
