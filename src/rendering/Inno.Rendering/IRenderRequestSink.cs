using System;

namespace Inno.Rendering;

/// <summary>
/// Accepts rendering-model-neutral requests without exposing runtime or backend ownership.
/// </summary>
public interface IRenderRequestSink
{
    /// <summary>
    /// Queues one immutable view request for the current or next render frame.
    /// </summary>
    /// <param name="request">
    /// Immutable pipeline-defined request.
    /// </param>
    void Submit(RenderRequest request);
}
