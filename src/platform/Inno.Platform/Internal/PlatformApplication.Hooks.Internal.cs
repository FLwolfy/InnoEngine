using Inno.Native.SDL3;

namespace Inno.Platform;

internal delegate void PlatformSdlEventHook(PlatformApplication application, ref SDLEvent sdlEvent);
internal delegate void PlatformApplicationHook(PlatformApplication application);
internal delegate void PlatformLiveResizeRedrawHook(PlatformApplication application, uint windowId);

internal static class PlatformApplicationHooks
{
    internal static PlatformSdlEventHook? s_onSdlEvent;
    internal static PlatformApplicationHook? s_onDisposing;
    internal static PlatformLiveResizeRedrawHook? s_onLiveResizeRedraw;

    internal static void DispatchSdlEvent(PlatformApplication application, ref SDLEvent sdlEvent)
    {
        s_onSdlEvent?.Invoke(application, ref sdlEvent);
    }

    internal static void OnDisposing(PlatformApplication application)
    {
        s_onDisposing?.Invoke(application);
    }

    internal static void DispatchLiveResizeRedraw(PlatformApplication application, uint windowId)
    {
        s_onLiveResizeRedraw?.Invoke(application, windowId);
    }
}
