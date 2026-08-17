using System;
using System.Diagnostics;
using System.IO;

using Inno.Assets;
using Inno.Core.Events;
using Inno.Core.Framework;
using Inno.Engine.Assets;
using Inno.Platform;
using Inno.Platform.ImGui;

namespace Inno.Editor.Application;

/// <summary>
/// Editor runtime host that wires Platform + Shell + ImGui + Editor layer.
/// </summary>
public sealed class EditorHost : IDisposable
{
    // NOTE: This project root should be changed to project selection
    private static readonly string PROJECT_ROOT = "/Users/aaronliao/Dev/GameEngineDev/InnoProject";
    
    private readonly PlatformApplication m_platformApplication;
    private readonly PlatformWindow m_window;
    private readonly Shell m_shell;
    private readonly PlatformImGuiContext m_imgui;
    private readonly EditorLayer m_editorLayer;
    private readonly string m_bootLogPath;
    private bool m_running;
    private bool m_disposed;
    private bool m_hasRenderedFrame;
    private int m_frameCount;

    /// <summary>
    /// Creates the editor host with default window and shell settings.
    /// </summary>
    public EditorHost()
    {
        m_bootLogPath = Path.Combine(Directory.GetCurrentDirectory(), "EditorBoot.log");
        BootLog("EditorHost ctor start.");
        m_platformApplication = new PlatformApplication();
        BootLog("PlatformApplication created.");
        m_window = m_platformApplication.CreateWindow(new PlatformWindowOptions
        {
            title = "Inno Editor",
            width = 1600,
            height = 900,
            resizable = true,
            highPixelDensity = true
        });
        BootLog($"Window created. id={m_window.windowId}, size={m_window.width}x{m_window.height}.");

        _ = typeof(SceneAsset).Assembly;
        m_shell = Shell.Initialize(new ShellSettings
        {
            fixedDeltaTime = 1f / 60f,
            maxFrameDeltaTime = 0.25f,
            maxUpdateStepsPerTick = 8,
            useSingleThreadJobSystem = false,
            jobWorkerCount = 0,
            projectRootDirectory = Path.GetFullPath(PROJECT_ROOT)
        });
        BootLog("Shell created.");

        m_imgui = m_platformApplication.CreateImGuiContext(
            m_window,
            ImGuiContextFlags.EnableViewports
            | ImGuiContextFlags.EnableDocking
            | ImGuiContextFlags.EnableSmoothResize);
        BootLog("ImGui context created.");

        BootLog($"AssetManager initialized={AssetManager.isInitialized} root='{AssetManager.assetRoot}'.");

        m_editorLayer = new EditorLayer(m_imgui);
        m_shell.layerStack.PushOverlay(m_editorLayer);
        m_running = true;
        BootLog("EditorHost ctor done.");
    }

    /// <summary>
    /// Runs the editor loop until window/application quit is requested.
    /// </summary>
    public int Run()
    {
        BootLog("Run loop start.");
        Stopwatch timer = Stopwatch.StartNew();
        double lastTime = 0.0;
        while (m_running)
        {
            double now = timer.Elapsed.TotalSeconds;
            float delta = (float)(now - lastTime);
            lastTime = now;
            if (delta < 0f)
                delta = 0f;

            while (m_platformApplication.PollEvent(out Event? evnt))
            {
                if (evnt is null)
                    continue;

                m_shell.eventDispatcher.Enqueue(evnt);
                if (ShouldClose(evnt))
                {
                    BootLog($"Exit requested by event: {evnt.GetType().Name}.");
                    m_running = false;
                    break;
                }
            }

            if (!m_running || m_window.isClosed)
            {
                if (m_window.isClosed)
                    BootLog("Main window is marked closed.");
                m_running = false;
                break;
            }

            if (m_frameCount == 0)
                BootLog("About to execute first shell.Tick.");

            m_shell.Tick((float)now, delta);

            if (m_frameCount == 0)
                BootLog("First shell.Tick completed.");

            m_hasRenderedFrame = true;
            m_frameCount++;
        }

        BootLog("Run loop end.");
        return 0;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (m_disposed)
            return;

        m_disposed = true;
        BootLog("Dispose start.");

        m_shell.layerStack.PopOverlay(m_editorLayer);
        m_platformApplication.DestroyImGuiContext(m_window);
        Shell.Shutdown();
        m_window.Dispose();
        m_platformApplication.Dispose();
        BootLog("Dispose end.");
    }

    private bool ShouldClose(Event evnt)
    {
        if (evnt is ApplicationQuitEvent)
        {
            // Ignore sporadic startup quit before first frame to avoid silent immediate exit.
            if (!m_hasRenderedFrame)
            {
                BootLog("Ignored early ApplicationQuitEvent before first frame.");
                return false;
            }
            return true;
        }

        return evnt is WindowCloseEvent closeEvent && closeEvent.windowId == m_window.windowId;
    }

    private void BootLog(string message)
    {
        string line = $"[{DateTime.Now:O}] {message}{Environment.NewLine}";
        Console.Write(line);
        File.AppendAllText(m_bootLogPath, line);
    }
}
