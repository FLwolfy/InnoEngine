using System;

namespace Inno.Platform.ImGui;

/// <summary>
/// Manages Dear ImGui frame lifecycle and rendering integration for a platform window.
/// </summary>
public sealed partial class PlatformImGuiContext : IDisposable
{
    /// <summary>
    /// Starts a new ImGui frame and updates input/state for the current window.
    /// </summary>
    /// <param name="deltaTimeSeconds">Elapsed time since the previous frame in seconds.</param>
    public partial void BeginFrame(float deltaTimeSeconds);

    /// <summary>
    /// Ends the current ImGui frame, renders it, and returns the native draw data pointer.
    /// </summary>
    /// <returns>The native pointer to <c>ImDrawData</c> for the current frame.</returns>
    public partial IntPtr EndFrame();

    /// <summary>
    /// Releases all unmanaged/native resources associated with this ImGui context.
    /// </summary>
    public partial void Dispose();
}
