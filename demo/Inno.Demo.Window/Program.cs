using System;
using System.Diagnostics;
using System.Numerics;

using Inno.Core.Events;
using Inno.Core.Framework;
using Inno.Core.Job;
using Inno.Core.Logging;
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
        using var shell = Shell.Initialize(new ShellSettings
        {
            fixedDeltaTime = 1f / 60f,
            maxFrameDeltaTime = 0.25f,
            maxUpdateStepsPerTick = 8,
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
                            Log.Info("[WindowEvent] {0} windowId={1}", windowEvent.GetType().Name, windowEvent.windowId);
                            break;
                        case KeyEvent keyEvent:
                            Log.Info("[KeyEvent] {0} windowId={1}", keyEvent.GetType().Name, keyEvent.windowId);
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
                JobSystem.RunOnMainThread(() => Log.Info("[JobSystem] Background job finished on main thread callback."));
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

        public override void OnLateUpdate(float deltaTime)
        {
            _ = m_imgui.RenderFrame(static () =>
            {
                _ = ImGui.DockSpaceOverViewport();
                ImGui.ShowDemoWindow();
                DrawNativeApiProbeWindow();
            });
        }

        private static void DrawNativeApiProbeWindow()
        {
            _ = ImGui.Begin("ImGui Native API Probe");

            ImGui.TextUnformatted("This panel prints native ImGui API values in different contexts.");
            DrawProbeBlock("Window Root (begin)");

            ImGui.TextUnformatted("After one Text item");
            DrawProbeBlock("Window Root (after item)");

            if (ImGui.BeginChild("##ProbeChild", new Vector2(0f, 220f), ImGuiChildFlags.Borders))
            {
                ImGui.TextUnformatted("Inside BeginChild");
                DrawProbeBlock("Child Root");

                if (ImGui.BeginTable("##ProbeTable", 2, ImGuiTableFlags.SizingStretchProp, new Vector2(0f, 0f)))
                {
                    ImGui.TableSetupColumn("Left", ImGuiTableColumnFlags.WidthStretch, 1f);
                    ImGui.TableSetupColumn("Right", ImGuiTableColumnFlags.WidthStretch, 1f);

                    ImGui.TableNextRow();
                    _ = ImGui.TableSetColumnIndex(0);
                    ImGui.TextUnformatted("Left Cell");
                    DrawProbeBlock("Table Cell Left");

                    _ = ImGui.TableSetColumnIndex(1);
                    ImGui.TextUnformatted("Right Cell");
                    DrawProbeBlock("Table Cell Right");

                    ImGui.EndTable();
                }
            }

            ImGui.EndChild();
            ImGui.End();
        }

        private static void DrawProbeBlock(string label)
        {
            ImGui.SeparatorText(label);

            Vector2 cursorPos = ImGui.GetCursorPos();
            Vector2 cursorScreenPos = ImGui.GetCursorScreenPos();
            Vector2 contentAvail = ImGui.GetContentRegionAvail();

            ImGui.TextUnformatted($"GetWindowWidth(): {ImGui.GetWindowWidth():F2}");
            ImGui.TextUnformatted($"GetWindowHeight(): {ImGui.GetWindowHeight():F2}");
            ImGui.TextUnformatted($"GetCursorPos(): {FormatVec2(cursorPos)}");
            ImGui.TextUnformatted($"GetCursorPosX/Y(): {ImGui.GetCursorPosX():F2}, {ImGui.GetCursorPosY():F2}");
            ImGui.TextUnformatted($"GetCursorScreenPos(): {FormatVec2(cursorScreenPos)}");
            ImGui.TextUnformatted($"GetContentRegionAvail(): {FormatVec2(contentAvail)}");
            ImGui.TextUnformatted($"GetScrollX/Y(): {ImGui.GetScrollX():F2}, {ImGui.GetScrollY():F2}");
            ImGui.TextUnformatted($"GetScrollMaxX/Y(): {ImGui.GetScrollMaxX():F2}, {ImGui.GetScrollMaxY():F2}");
        }

        private static string FormatVec2(Vector2 value)
        {
            return $"({value.X:F2}, {value.Y:F2})";
        }
    }
}
