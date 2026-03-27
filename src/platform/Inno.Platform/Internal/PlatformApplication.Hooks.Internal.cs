using Inno.Native.SDL3;

namespace Inno.Platform;

internal delegate void PlatformSdlEventHook(PlatformApplication application, ref SDLEvent sdlEvent);
internal delegate void PlatformApplicationHook(PlatformApplication application);

internal static class PlatformApplicationHooks
{
    internal static PlatformSdlEventHook? s_onSdlEvent;
    internal static PlatformApplicationHook? s_onDisposing;

    internal static void DispatchSdlEvent(PlatformApplication application, ref SDLEvent sdlEvent)
    {
        s_onSdlEvent?.Invoke(application, ref sdlEvent);
    }

    internal static void OnDisposing(PlatformApplication application)
    {
        s_onDisposing?.Invoke(application);
    }
}
