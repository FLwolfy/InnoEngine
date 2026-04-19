using System;
using System.Diagnostics;

using Inno.Core.Events;
using Inno.Core.Framework;
using Inno.Core.Job;
using Inno.Native.ImGui;
using Inno.Platform;
using Inno.Platform.ImGui;

namespace Inno.Demo.Window;

internal static class Program
{
    private static int Main()
    {
        using var platformApp = new PlatformApplication();
        using var window = platformApp.CreateWindow(new PlatformWindowOptions
        {
            title = "Inno Demo (App + Shell + Layer + JobSystem)",
            width = 1280,
            height = 720,
            resizable = true,
            highPixelDensity = true
        });
        using var shell = new Shell(new ShellSettings
        {
            fixedDeltaTime = 1f / 60f,
            maxFrameDeltaTime = 0.25f,
            useSingleThreadJobSystem = false,
            jobWorkerCount = 0
        });
        var imgui = platformApp.CreateImGuiContext(
            window,
            ImGuiContextFlags.EnableViewports | ImGuiContextFlags.EnableDocking | ImGuiContextFlags.EnableSmoothResize
        );

        var jobLayer = new JobDemoLayer();
        var imguiLayer = new ImGuiDemoLayer(imgui);
        shell.layerStack.PushLayer(jobLayer);
        shell.layerStack.PushOverlay(imguiLayer);

        try
        {
            var isRunning = true;
            var timer = Stopwatch.StartNew();
            var lastTime = 0.0;
            while (isRunning)
            {
                var now = timer.Elapsed.TotalSeconds;
                var delta = (float)(now - lastTime);
                lastTime = now;
                if (delta < 0f)
                {
                    delta = 0f;
                }

                while (platformApp.PollEvent(out var evnt))
                {
                    if (evnt is null)
                    {
                        continue;
                    }

                    switch (evnt)
                    {
                        case WindowEvent windowEvent:
                            Console.WriteLine($"[WindowEvent] {windowEvent.GetType().Name} windowId={windowEvent.windowId}");
                            break;
                        case KeyEvent keyEvent:
                            Console.WriteLine($"[KeyEvent] {keyEvent.GetType().Name} windowId={keyEvent.windowId}");
                            break;
                    }

                    shell.eventDispatcher.Enqueue(evnt);
                    if (evnt is ApplicationQuitEvent)
                    {
                        isRunning = false;
                        break;
                    }

                    if (evnt is WindowCloseEvent closeEvent && closeEvent.windowId == window.windowId)
                    {
                        isRunning = false;
                        break;
                    }
                }

                if (!isRunning || window.isClosed)
                {
                    isRunning = false;
                    break;
                }

                shell.Tick((float)now, delta);
            }
        }
        finally
        {
            platformApp.DestroyImGuiContext(window);
            shell.layerStack.PopOverlay(imguiLayer);
            shell.layerStack.PopLayer(jobLayer);
        }

        return 0;
    }

    private sealed class JobDemoLayer : Layer
    {
        private bool m_scheduledJobDemo;
        private float m_elapsedSeconds;

        internal JobDemoLayer()
            : base("JobDemoLayer")
        {
        }

        public override void OnUpdate(float deltaTime)
        {
            if (m_scheduledJobDemo)
            {
                return;
            }

            m_elapsedSeconds += deltaTime;
            if (m_elapsedSeconds < 5f)
            {
                return;
            }

            m_scheduledJobDemo = true;
            _ = JobSystem.Schedule(() =>
            {
                JobSystem.EnqueueMainThread(() => Console.WriteLine("[JobSystem] Background job finished on main thread callback."));
            });
        }
    }

    private sealed class ImGuiDemoLayer : Layer
    {
        private readonly PlatformImGuiContext m_imgui;

        internal ImGuiDemoLayer(PlatformImGuiContext imgui)
            : base("ImGuiDemoLayer")
        {
            m_imgui = imgui;
        }

        public override void OnRender(float renderDeltaTime)
        {
            _ = m_imgui.RenderFrame(static () =>
            {
                _ = ImGui.DockSpaceOverViewport();
                ImGui.ShowDemoWindow();
            });
        }
    }
}
