using System.Diagnostics;

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
        using var imgui = app.CreateImGuiContext(window, enableViewports: true, enableDocking: true);

        var timer = Stopwatch.StartNew();
        var lastTime = timer.Elapsed;
        var isRunning = true;
        while (isRunning && !window.isClosed)
        {
            while (app.PollEvent(out var evnt))
            {
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

            var now = timer.Elapsed;
            var deltaSeconds = (float)(now - lastTime).TotalSeconds;
            lastTime = now;

            imgui.BeginFrame(deltaSeconds);
            _ = ImGui.DockSpaceOverViewport();
            ImGui.ShowDemoWindow();
            var drawData = imgui.EndFrame();
            _ = drawData;
        }

        app.DestroyImGuiContext(window);
        return 0;
    }

}
