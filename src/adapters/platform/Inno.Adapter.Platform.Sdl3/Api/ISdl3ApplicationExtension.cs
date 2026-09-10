using System;

namespace Inno.Adapter.Platform.Sdl3;

/// <summary>
/// Defines the synchronous SDL3 callbacks required by an SDL3-specific integration.
/// </summary>
public interface ISdl3ApplicationExtension
{
    /// <summary>
    /// Processes one backend-native event before it is translated into an engine event.
    /// </summary>
    /// <param name="application">
    /// The platform application dispatching the event.
    /// </param>
    /// <param name="nativeEventData">
    /// A read-only view over one SDL event. The span is valid only for this callback and must not be retained.
    /// </param>
    void ProcessNativeEvent(
        Sdl3PlatformApplication application,
        scoped ReadOnlySpan<byte> nativeEventData);

    /// <summary>
    /// Synchronizes integration window state before the host renders a complete live-resize frame.
    /// </summary>
    /// <param name="application">
    /// The platform application requesting the redraw.
    /// </param>
    /// <param name="windowId">
    /// The platform window identifier being resized.
    /// </param>
    void PrepareLiveResizeWindow(Sdl3PlatformApplication application, uint windowId);

    /// <summary>
    /// Releases application-bound integration state before platform resources are destroyed.
    /// </summary>
    /// <param name="application">
    /// The platform application being disposed.
    /// </param>
    void OnApplicationDisposing(Sdl3PlatformApplication application);
}
