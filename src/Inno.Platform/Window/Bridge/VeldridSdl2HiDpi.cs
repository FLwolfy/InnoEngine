using System;
using System.Runtime.InteropServices;

using Inno.Core.Math;

using Veldrid.Sdl2;

namespace Inno.Platform.Window.Bridge;

/// <summary>
/// SDL2 HiDPI helpers.
/// 
/// Motivation:
/// - On macOS (Retina) SDL windows have a logical size (points) and a drawable size (pixels).
/// - If you resize swapchains and drive ImGui using logical size only, rendering happens at 1x and the OS upscales -> blurry text.
/// </summary>
internal static unsafe class VeldridSdl2HiDpi
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void SdlGetWindowSizeInPixelsT(IntPtr window, int* w, int* h);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void SdlGlGetDrawableSizeT(IntPtr window, int* w, int* h);

    private static SdlGetWindowSizeInPixelsT? m_getWindowSizeInPixels;
    private static SdlGlGetDrawableSizeT? m_glGetDrawableSize;

    public static Vector2Int GetFramebufferSize(Sdl2Window window)
    {
        if (window == null) throw new ArgumentNullException(nameof(window));

        // Prefer SDL_GetWindowSizeInPixels (SDL 2.26+)
        if (m_getWindowSizeInPixels == null)
        {
            try { m_getWindowSizeInPixels = Sdl2Native.LoadFunction<SdlGetWindowSizeInPixelsT>("SDL_GetWindowSizeInPixels"); }
            catch { m_getWindowSizeInPixels = null; }
        }

        int w = 0, h = 0;

        if (m_getWindowSizeInPixels != null)
        {
            m_getWindowSizeInPixels(window.SdlWindowHandle, &w, &h);
            if (w > 0 && h > 0) return new(w, h);
        }

        // Fallback: SDL_GL_GetDrawableSize
        if (m_glGetDrawableSize == null)
        {
            try { m_glGetDrawableSize = Sdl2Native.LoadFunction<SdlGlGetDrawableSizeT>("SDL_GL_GetDrawableSize"); }
            catch { m_glGetDrawableSize = null; }
        }

        if (m_glGetDrawableSize != null)
        {
            m_glGetDrawableSize(window.SdlWindowHandle, &w, &h);
            if (w > 0 && h > 0) return new(w, h);
        }

        // Last resort: logical size
        return new(window.Width, window.Height);
    }

    public static Vector2 GetFramebufferScale(Sdl2Window window)
    {
        var size = GetFramebufferSize(window);
        float sx = window.Width > 0 ? (float)size.x / window.Width : 1f;
        float sy = window.Height > 0 ? (float)size.y / window.Height : 1f;
        if (sx <= 0f) sx = 1f;
        if (sy <= 0f) sy = 1f;
        return new(sx, sy);
    }
}
