using System;

using Inno.Native.SDL3;

namespace Inno.Platform.ImGui;

internal static unsafe class PlatformWindowSdlAccess
{
    internal static SDLWindowPtr GetSdlWindow(this PlatformWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);
        PlatformNativeHandles handles = window.nativeHandles;
        if (!string.Equals(handles.backendName, "SDL3", StringComparison.Ordinal) ||
            handles.backendWindowHandle == IntPtr.Zero)
        {
            throw new InvalidOperationException("The ImGui SDL backend requires an SDL3 platform window.");
        }
        return new SDLWindowPtr((SDLWindow*)handles.backendWindowHandle.ToPointer());
    }
}
