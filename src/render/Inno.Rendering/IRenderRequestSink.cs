using System;

namespace Inno.Rendering;

/// <summary>
/// Accepts camera and editor view requests without exposing pipeline or backend ownership.
/// </summary>
public interface IRenderRequestSink
{
    /// <summary>Queues one immutable view request for the current or next render frame.</summary>
    /// <param name="request">Camera, preview, Scene View or Game View request.</param>
    void Submit(RenderRequest request);
}
