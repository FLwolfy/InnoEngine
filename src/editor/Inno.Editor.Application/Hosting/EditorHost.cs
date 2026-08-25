using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

using Inno.Assets;
using Inno.Core.Events;
using Inno.Core.Framework;
using Inno.Core.Logging;
using Inno.Editor.Core;
using Inno.Platform;
using Inno.Platform.ImGui;

namespace Inno.Editor.Application;

/// <summary>
/// Editor runtime host that wires Platform + Shell + ImGui + Editor layer.
/// </summary>
internal sealed class EditorHost : IDisposable
{
    private const string C_LOG_DIRECTORY_NAME = "Logs";
    private const string C_BOOT_LOG_FILE_NAME = "EditorBoot.log";

    private readonly PlatformApplication m_platformApplication;
    private readonly PlatformWindow m_window;
    private readonly HashSet<uint> m_focusedWindowIds = [];
    private readonly Shell m_shell;
    private readonly PlatformImGuiContext m_imgui;
    private readonly EditorLayer m_editorLayer;
    private readonly EditorHostResourceStack m_resources;
    private readonly string m_bootLogPath;
    private bool m_running;
    private bool m_disposed;
    private bool m_hasRenderedFrame;
    private bool m_shutdownStateSaved;
    private int m_frameCount;

    private EditorHost(
        string projectDirectory,
        string bootLogPath,
        PlatformApplication platformApplication,
        PlatformWindow window,
        Shell shell,
        PlatformImGuiContext imgui,
        EditorLayer editorLayer,
        EditorHostResourceStack resources)
    {
        this.projectDirectory = projectDirectory;
        m_bootLogPath = bootLogPath;
        m_platformApplication = platformApplication;
        m_window = window;
        m_shell = shell;
        m_imgui = imgui;
        m_editorLayer = editorLayer;
        m_resources = resources;
        if (m_window.isFocused)
            m_focusedWindowIds.Add(m_window.windowId);
        m_running = true;
    }

    internal static EditorHost Create(string projectDirectory)
    {
        string normalizedProject = PrepareProjectDirectory(projectDirectory);
        string logDirectory = Path.Combine(normalizedProject, C_LOG_DIRECTORY_NAME);
        Directory.CreateDirectory(logDirectory);
        string bootLogPath = Path.Combine(logDirectory, C_BOOT_LOG_FILE_NAME);
        var resources = new EditorHostResourceStack(
            exception => AppendBootLog(bootLogPath, $"Teardown failure: {exception}"));
        bool overlayPushed = false;
        try
        {
            AppendBootLog(bootLogPath, "EditorHost creation start.");
            PlatformApplication platform = resources.Acquire(
                static () => new PlatformApplication(),
                static application => application.Dispose());
            PlatformWindow window = resources.Acquire(
                () => platform.CreateWindow(new PlatformWindowOptions
                {
                    title = "Inno Editor",
                    width = 1600,
                    height = 900,
                    resizable = true,
                    highPixelDensity = true
                }),
                static createdWindow => createdWindow.Dispose());
            Shell shell = resources.Acquire(
                () => Shell.Initialize(new ShellSettings
                {
                    fixedDeltaTime = 1f / 60f,
                    maxFrameDeltaTime = 0.25f,
                    maxUpdateStepsPerTick = 8,
                    useSingleThreadJobSystem = false,
                    jobWorkerCount = 0,
                    projectRootDirectory = normalizedProject
                }),
                static _ =>
                {
                    if (Shell.isInitialized)
                        Shell.Shutdown();
                });
            PlatformImGuiContext imgui = resources.Acquire(
                () => platform.CreateImGuiContext(
                    window,
                    ImGuiContextFlags.EnableViewports |
                    ImGuiContextFlags.EnableDocking |
                    ImGuiContextFlags.EnableSmoothResize),
                _ => platform.DestroyImGuiContext(window));
            var editorContext = new EditorContext(normalizedProject);
            imgui.SetIniFile(null);
            imgui.LoadIniSettings(editorContext.imguiLayout);
            EditorLayer layer = new EditorLayer(imgui, editorContext)
            {
                isFocused = window.isFocused
            };
            resources.Register(() =>
            {
                if (overlayPushed)
                    shell.layerStack.PopOverlay(layer);
                else
                    layer.DisposeUnattached();
            });
            shell.layerStack.PushOverlay(layer);
            overlayPushed = true;
            if (layer.panelCount == 0)
                throw new InvalidOperationException("No editor panels were discovered from the active host assemblies.");
            var host = new EditorHost(
                normalizedProject,
                bootLogPath,
                platform,
                window,
                shell,
                imgui,
                layer,
                resources);
            host.BootLog($"Editor layer attached with {layer.panelCount} panel(s).");
            host.BootLog($"AssetManager initialized={AssetManager.isInitialized} root='{AssetManager.assetRoot}'.");
            return host;
        }
        catch
        {
            resources.Dispose();
            throw;
        }
    }

    /// <summary>Gets the normalized project directory owned by this host.</summary>
    public string projectDirectory { get; }

    /// <summary>
    /// Runs the editor loop until window/application quit is requested.
    /// </summary>
    /// <returns>The process exit code produced after the main editor window closes.</returns>
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

                UpdateFocusedWindows(evnt);
                if (ShouldClose(evnt))
                {
                    BootLog($"Exit requested by event: {evnt.GetType().Name}.");
                    SaveBeforeShutdown();
                    m_running = false;
                    break;
                }
                m_shell.eventDispatcher.Enqueue(evnt);
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

            m_editorLayer.isFocused = HasEditorFocus();
            m_shell.Tick((float)now, delta);

            if (m_frameCount == 0)
                BootLog("First shell.Tick completed.");

            m_hasRenderedFrame = true;
            m_frameCount++;
        }

        SaveBeforeShutdown();
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

        m_resources.Dispose();
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

    private static string PrepareProjectDirectory(string projectDirectory)
    {
        if (string.IsNullOrWhiteSpace(projectDirectory))
            throw new ArgumentException("Project directory is required.", nameof(projectDirectory));
        string normalizedPath = Path.GetFullPath(projectDirectory);
        if (File.Exists(normalizedPath))
            throw new IOException($"Project directory '{normalizedPath}' points to a file.");
        Directory.CreateDirectory(normalizedPath);
        return normalizedPath;
    }

    private bool HasEditorFocus()
        => m_focusedWindowIds.Count > 0;

    private void SaveBeforeShutdown()
    {
        if (m_shutdownStateSaved)
            return;

        m_shutdownStateSaved = m_editorLayer.PrepareShutdown();
        BootLog(m_shutdownStateSaved
            ? "Project editor state frozen and saved before shutdown."
            : "Project editor state save failed before shutdown.");
    }

    private void UpdateFocusedWindows(Event evnt)
    {
        if (evnt is WindowFocusChangedEvent focusChanged)
        {
            if (focusChanged.isFocused)
                m_focusedWindowIds.Add(focusChanged.windowId);
            else
                m_focusedWindowIds.Remove(focusChanged.windowId);
        }
        else if (evnt is WindowCloseEvent closeEvent)
        {
            m_focusedWindowIds.Remove(closeEvent.windowId);
        }
    }

    private void BootLog(string message)
        => AppendBootLog(m_bootLogPath, message);

    private static void AppendBootLog(string bootLogPath, string message)
    {
        string line = $"[{DateTime.Now:O}] {message}{Environment.NewLine}";
        Console.Write(line);
        File.AppendAllText(bootLogPath, line);
    }

}
