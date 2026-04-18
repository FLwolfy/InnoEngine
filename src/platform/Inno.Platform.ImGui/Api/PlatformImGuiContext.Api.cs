using System;

namespace Inno.Platform.ImGui;

/// <summary>
/// Manages Dear ImGui frame lifecycle and rendering integration for a platform window.
/// </summary>
public sealed partial class PlatformImGuiContext : IDisposable
{
    /// <summary>
    /// Renders one ImGui frame by running the provided draw callback.
    /// </summary>
    /// <param name="drawFrame">Draw callback that issues ImGui commands for this frame.</param>
    /// <returns>The native pointer to <c>ImDrawData</c> for the rendered frame.</returns>
    public partial IntPtr RenderFrame(Action drawFrame);

    /// <summary>
    /// Releases all unmanaged/native resources associated with this ImGui context.
    /// </summary>
    public partial void Dispose();
}
