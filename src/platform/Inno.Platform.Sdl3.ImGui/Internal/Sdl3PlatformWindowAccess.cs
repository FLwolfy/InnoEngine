using System;
using Inno.Native.Sdl3;

namespace Inno.Platform.Sdl3.ImGui;

internal static unsafe class Sdl3PlatformWindowAccess
{
    internal static SDLWindowPtr GetSdlWindow(this Sdl3PlatformWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);
        if (window.sdlWindowHandle == 0)
        {
            throw new ObjectDisposedException(nameof(window));
        }

        return new SDLWindowPtr((SDLWindow*)window.sdlWindowHandle);
    }
}
