using Inno.Graphics;
using Inno.Graphics.Bgfx;
using Inno.Platform;
using Inno.Platform.SDL3;
using System.Diagnostics;

namespace Inno.Demo.Window;

internal static class Program
{
    private static int Main()
    {
        using var app = new Sdl3PlatformApplication();
        using var window = app.CreateWindow(new PlatformWindowOptions
        {
            title = "Inno Demo",
            width = 1280,
            height = 720,
            resizable = true,
            highPixelDensity = true
        });

        using var device = new BgfxGraphicsDevice();
        using var swapchain = device.CreateSwapchain(new GraphicsSwapchainDescription
        {
            nativeHandle = window.nativeHandles.windowHandle,
            nativeDisplayHandle = window.nativeHandles.displayHandle,
            nativeWindowKind = window.nativeHandles.handleKind switch
            {
                PlatformNativeHandleKind.Win32 => GraphicsNativeWindowKind.Win32,
                PlatformNativeHandleKind.Cocoa => GraphicsNativeWindowKind.Cocoa,
                PlatformNativeHandleKind.Wayland => GraphicsNativeWindowKind.Wayland,
                PlatformNativeHandleKind.X11 => GraphicsNativeWindowKind.X11,
                _ => GraphicsNativeWindowKind.Unknown
            },
            width = window.width,
            height = window.height,
            colorFormat = PixelFormat.B8G8R8A8Unorm,
            depthFormat = PixelFormat.D24UnormS8Uint,
            vSync = true
        });

        var timer = Stopwatch.StartNew();
        var isRunning = true;
        while (isRunning && !window.isClosed)
        {
            while (app.PollEvent(out var evnt))
            {
                if (evnt.type == PlatformEventType.QuitRequested || evnt.type == PlatformEventType.WindowCloseRequested)
                {
                    isRunning = false;
                    break;
                }

                if (evnt.type == PlatformEventType.WindowResized && evnt.width > 0 && evnt.height > 0)
                {
                    // renderer.Resize(evnt.width, evnt.height);
                }
            }

            if (!isRunning)
            {
                break;
            }

            var t = (float)timer.Elapsed.TotalSeconds;
        }

        return 0;
    }
}
