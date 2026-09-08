using System;

using Inno.Rendering;

namespace Inno.Rendering;

/// <summary>
/// Streams immutable CPU data into reusable GPU pages for the current frame.
/// </summary>
public interface IRenderFrameUploadService
{
    /// <summary>
    /// Uploads complete elements and returns a slice valid only during the current frame.
    /// </summary>
    /// <param name="descriptor">
    /// Element interpretation and permitted GPU uses.
    /// </param>
    /// <param name="data">
    /// Tightly packed complete element bytes.
    /// </param>
    /// <param name="name">
    /// Diagnostic name used when a backing page is allocated.
    /// </param>
    /// <returns>
    /// A current-frame slice covering the uploaded elements.
    /// </returns>
    RenderBufferSlice UploadBuffer(
        RenderBufferUploadDescriptor descriptor,
        ReadOnlyMemory<byte> data,
        string name);
}
