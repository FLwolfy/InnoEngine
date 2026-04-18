using System;
using Inno.Core.Events;
using Inno.Platform;
using Inno.Platform.ImGui;
using Inno.Native.ImGui;

namespace Inno.Demo.Window;

internal static class Program
{
    private static int Main()
    {
        using var app = new PlatformApplication();
        using var window = app.CreateWindow(new PlatformWindowOptions
        {
            title = "Inno Demo",
            width = 1280,
            height = 720,
            resizable = true,
            highPixelDensity = true
        });
        using var imgui = app.CreateImGuiContext(
            window,
            ImGuiContextFlags.EnableViewports | ImGuiContextFlags.EnableDocking | ImGuiContextFlags.EnableSmoothResize);
        static void DrawUi()
        {
            _ = ImGui.DockSpaceOverViewport();
            ImGui.ShowDemoWindow();
        }
        var isRunning = true;
        while (isRunning && !window.isClosed)
        {
            while (app.PollEvent(out var evnt))
            {
                switch (evnt)
                {
                    case WindowEvent windowEvent:
                        Console.WriteLine($"[WindowEvent] {windowEvent.GetType().Name} windowId={windowEvent.windowId}");
                        break;
                    case KeyEvent keyEvent:
                        Console.WriteLine($"[KeyEvent] {keyEvent.GetType().Name} windowId={keyEvent.windowId}");
                        break;
                    case MouseEvent mouseEvent:
                        Console.WriteLine($"[MouseEvent] {mouseEvent.GetType().Name} windowId={mouseEvent.windowId}");
                        break;
                }

                if (evnt is ApplicationQuitEvent or WindowCloseEvent)
                {
                    isRunning = false;
                    break;
                }
            }

            if (!isRunning)
            {
                break;
            }

            var drawData = imgui.RenderFrame(DrawUi);
            _ = drawData;
        }

        app.DestroyImGuiContext(window);
        return 0;
    }

}
