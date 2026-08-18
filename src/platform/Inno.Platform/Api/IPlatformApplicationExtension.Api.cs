namespace Inno.Platform;

/// <summary>
/// Receives narrowly scoped platform callbacks required by optional backend integrations.
/// </summary>
public interface IPlatformApplicationExtension
{
    /// <summary>
    /// Processes one backend-native event before it is translated into an engine event.
    /// </summary>
    /// <param name="application">The platform application dispatching the event.</param>
    /// <param name="nativeEvent">An opaque native event valid only for the duration of this callback.</param>
    void ProcessNativeEvent(PlatformApplication application, PlatformNativeEvent nativeEvent);

    /// <summary>
    /// Redraws integration content while a native window is in a live resize loop.
    /// </summary>
    /// <param name="application">The platform application requesting the redraw.</param>
    /// <param name="windowId">The platform window identifier being resized.</param>
    void RenderLiveResizeWindow(PlatformApplication application, uint windowId);

    /// <summary>
    /// Releases application-bound integration state before platform resources are destroyed.
    /// </summary>
    /// <param name="application">The platform application being disposed.</param>
    void OnApplicationDisposing(PlatformApplication application);
}
