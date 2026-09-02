using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

using Inno.Assets;
using Inno.Assets.Pipeline;
using Inno.Build;
using Inno.Build.Platform.MacOS;
using Inno.Build.Platform.Windows;
using Inno.Build.Toolchains.Bgfx.Tools;
using Inno.Core.Events;
using Inno.Core.Logging;
using Inno.Core.Settings;
using Inno.Editor.Core;
using Inno.Editor.Rendering;
using Inno.Runtime;
using Inno.Scene;
using Inno.Platform;
using Inno.Platform.Sdl3;
using Inno.Platform.Sdl3.ImGui;
using Inno.Rendering;
using Inno.Rendering.Assets;
using Inno.Rendering.Bgfx;
using Inno.Rendering.Bgfx.ImGui;
using Inno.Rendering.Runtime;
using Inno.Rendering.ShaderGraph;

namespace Inno.Editor.Application;

/// <summary>
/// Editor composition root that owns Platform, authoring services, runtime sessions, rendering, and presentation.
/// </summary>
internal sealed class EditorHost : IDisposable
{
    private const string C_LOG_DIRECTORY_NAME = "Logs";
    private const string C_BOOT_LOG_FILE_NAME = "EditorBoot.log";

    private readonly Sdl3PlatformApplication m_platformApplication;
    private readonly Sdl3PlatformWindow m_window;
    private readonly HashSet<uint> m_focusedWindowIds = [];
    private readonly RuntimeSession m_editSession;
    private readonly EditorAuthoringServices m_authoring;
    private readonly EditorHostLayerStack m_layers;
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
        Sdl3PlatformApplication platformApplication,
        Sdl3PlatformWindow window,
        RuntimeSession editSession,
        EditorAuthoringServices authoring,
        EditorHostLayerStack layers,
        EditorLayer editorLayer,
        EditorHostResourceStack resources)
    {
        this.projectDirectory = projectDirectory;
        m_bootLogPath = bootLogPath;
        m_platformApplication = platformApplication;
        m_window = window;
        m_editSession = editSession;
        m_authoring = authoring;
        m_layers = layers;
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
        bool renderingLayerPushed = false;
        bool overlayPushed = false;
        BgfxImGuiRenderer? bgfxImGui = null;
        try
        {
            AppendBootLog(bootLogPath, "EditorHost creation start.");
            Sdl3PlatformApplication platform = resources.Acquire(
                static () => new Sdl3PlatformApplication(),
                static application => application.Dispose());
            Sdl3PlatformWindow window = resources.Acquire(
                () => platform.CreateWindow(new PlatformWindowOptions
                {
                    title = "Inno Editor",
                    width = 1600,
                    height = 900,
                    resizable = true,
                    highPixelDensity = true
                }),
                static createdWindow => createdWindow.Dispose());
            EngineHost engineHost = resources.Acquire(
                () => new EngineHostBuilder()
                    .UseMetadataCache(Path.Combine(normalizedProject, "Library", "Assemblies"))
                    .Build(),
                static host => host.Dispose());
            RuntimeSession editSession = resources.Acquire(
                () => engineHost.CreateSession(new RuntimeSessionOptions
                {
                    kind = RuntimeSessionKind.Edit,
                    applicationId = "inno.editor",
                    persistentDataDirectory = Path.Combine(
                        normalizedProject,
                        "Library",
                        "PersistentData",
                        "inno.editor"),
                    fixedDeltaTime = 1f / 60f,
                    maxFrameDeltaTime = 0.25f,
                    maxFixedStepsPerFrame = 8,
                    jobExecutionMode = RuntimeJobExecutionMode.WorkerPool
                }),
                static session => session.Dispose());
            _ = resources.Acquire(
                editSession.EnterExecutionScope,
                static scope => scope.Dispose());
            var consoleLog = new ConsoleLogSink();
            engineHost.logs.RegisterSink(consoleLog);
            resources.Register(() => engineHost.logs.UnregisterSink(consoleLog));
            EditorAuthoringServices authoring = resources.Acquire(
                () => EditorAuthoringServices.Start(normalizedProject, engineHost),
                static services => services.Dispose());
            _ = resources.Acquire(
                () => AssetExecutionContext.EnterScope(authoring.assets),
                static scope => scope.Dispose());
            _ = resources.Acquire(
                () => ProjectSettingsExecutionContext.EnterScope(authoring.settings),
                static scope => scope.Dispose());
            var buildPipeline = new BuildPipeline(
                authoring.assets,
                authoring.plugins,
                authoring.settings,
                engineHost.serialization,
                authoring.compiler,
                ResolveSupportPackRoot(),
                [
                    new MacOSArm64GameBuildTarget(authoring.assets, engineHost.serialization),
                    new WindowsX64GameBuildTarget(authoring.assets, engineHost.serialization)
                ]);
            var buildProfiles = new BuildProfileStore(
                Path.Combine(normalizedProject, "BuildProfile.inno"),
                engineHost.serialization);
            EditorHostLayerStack layers = resources.Acquire(
                () => new EditorHostLayerStack(() => editSession.events.CreateHub()),
                static stack => stack.Dispose());
            BgfxDevice graphicsDevice = resources.Acquire(
                () => new BgfxDevice(new BgfxDeviceOptions
                {
                    window = window,
                    verticalSync = true,
                    sRgbBackbuffer = true
                }),
                static device => device.Dispose());
            resources.Register(() =>
            {
                graphicsDevice.BeginFrame();
                try
                {
                    bgfxImGui?.PrepareFrame(ulong.MaxValue);
                }
                finally
                {
                    _ = graphicsDevice.EndFrame();
                }
            });
            var shaderCompiler = new ShaderCompiler(new BgfxShadercToolchain());
            var textureCompiler = new BgfxTextureTargetCompiler();
            GraphicsPipelineDescriptor imguiPipeline = EditorShaderBootstrap.Compile(
                shaderCompiler,
                graphicsDevice.capabilities,
                Path.Combine(normalizedProject, "Assets"));
            EditorRenderDiagnosticSink renderDiagnostics = resources.Acquire(
                () => new EditorRenderDiagnosticSink(engineHost.diagnostics),
                static sink => sink.Dispose());
            var renderArtifacts = resources.Acquire(
                () => new EditorRenderTargetArtifactProvider(
                    authoring.assets,
                    engineHost.serialization,
                    shaderCompiler,
                    textureCompiler,
                    renderDiagnostics),
                static provider => provider.Dispose());
            bgfxImGui = resources.Acquire(
                () => new BgfxImGuiRenderer(graphicsDevice, imguiPipeline),
                static renderer => renderer.Dispose());
            PlatformImGuiContext imgui = resources.Acquire(
                () => platform.CreateImGuiContext(
                    window,
                    ImGuiContextFlags.EnableViewports |
                    ImGuiContextFlags.EnableDocking |
                    ImGuiContextFlags.EnableSmoothResize,
                    bgfxImGui),
                _ => platform.DestroyImGuiContext(window));
            var renderingLayer = new RenderRuntimeLayer(
                engineHost.types,
                graphicsDevice,
                renderDiagnostics,
                contributors: [bgfxImGui],
                targetArtifacts: renderArtifacts);
            var renderingAdapter = new EditorRenderingLayer(renderingLayer);
            var reloadCoordinator = new EditorReloadCoordinator();
            var renderingHost = resources.Acquire(
                () => new EditorRenderingHostService(
                    renderingLayer,
                    bgfxImGui,
                    imgui,
                    reloadCoordinator),
                static service => service.Dispose());
            var shaderNodes = resources.Acquire(
                () => new ShaderNodeRegistry(engineHost.types),
                static registry => registry.Dispose());
            shaderNodes.RefreshExtensions();
            var editorContext = new EditorContext(normalizedProject);
            imgui.SetIniFile(null);
            imgui.LoadIniSettings(editorContext.imguiLayout);
            var playOptions = new RuntimeSessionOptions
            {
                kind = RuntimeSessionKind.Play,
                applicationId = "inno.editor.play",
                persistentDataDirectory = Path.Combine(
                    normalizedProject,
                    "Library",
                    "PersistentData",
                    "inno.editor.play"),
                fixedDeltaTime = 1f / 60f,
                maxFrameDeltaTime = 0.25f,
                maxFixedStepsPerFrame = 8,
                jobExecutionMode = RuntimeJobExecutionMode.WorkerPool
            };
            EditorLayer layer = new EditorLayer(
                imgui,
                editorContext,
                engineHost.types,
                engineHost.logs,
                [
                    renderingHost,
                    shaderNodes,
                    reloadCoordinator,
                    engineHost,
                    engineHost.modules,
                    engineHost.diagnostics,
                    editSession,
                    playOptions,
                    authoring.assets,
                    authoring.plugins,
                    authoring.settings,
                    authoring.compiler,
                    buildPipeline,
                    buildProfiles,
                    engineHost.types,
                    engineHost.serialization
                ])
            {
                isFocused = window.isFocused
            };
            resources.Register(() =>
            {
                if (renderingLayerPushed)
                    layers.PopLayer(renderingAdapter);
            });
            layers.PushLayer(renderingAdapter);
            renderingLayerPushed = true;
            resources.Register(() =>
            {
                if (overlayPushed)
                    layers.PopOverlay(layer);
                else
                    layer.DisposeUnattached();
            });
            layers.PushOverlay(layer);
            overlayPushed = true;
            if (layer.panelCount == 0)
                throw new InvalidOperationException("No editor panels were discovered from the active host assemblies.");
            var host = new EditorHost(
                normalizedProject,
                bootLogPath,
                platform,
                window,
                editSession,
                authoring,
                layers,
                layer,
                resources);
            host.BootLog($"Editor layer attached with {layer.panelCount} panel(s).");
            host.BootLog(
                $"Rendering initialized with {graphicsDevice.capabilities.backend} " +
                $"(views={graphicsDevice.capabilities.limits.maxViews}).");
            host.BootLog(
                $"AssetPipeline initialized={authoring.assets.isInitialized} " +
                $"root='{authoring.assets.assetRoot}'.");
            return host;
        }
        catch
        {
            resources.Dispose();
            throw;
        }
    }

    private static string ResolveSupportPackRoot()
    {
        string? configured = Environment.GetEnvironmentVariable("INNO_SUPPORT_PACK_ROOT");
        return string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(AppContext.BaseDirectory, "SupportPacks")
            : Path.GetFullPath(configured);
    }

    /// <summary>
    /// Gets the normalized project directory owned by this host.
    /// </summary>
    public string projectDirectory { get; }

    /// <summary>
    /// Executes the configured workflow and returns its process outcome.
    /// </summary>
    /// <param name="smokeFrameLimit">
    /// Optional positive frame count used by automated native smoke tests; <see langword="null"/> runs interactively.
    /// </param>
    /// <returns>
    /// The process exit code produced after the main editor window closes.
    /// </returns>
    public int Run(int? smokeFrameLimit = null)
    {
        if (smokeFrameLimit is <= 0)
            throw new ArgumentOutOfRangeException(nameof(smokeFrameLimit));

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
                m_editSession.events.Enqueue(evnt);
            }

            if (!m_running || m_window.isClosed)
            {
                if (m_window.isClosed)
                    BootLog("Main window is marked closed.");
                m_running = false;
                break;
            }

            if (m_frameCount == 0)
                BootLog("About to execute first editor frame.");

            m_editorLayer.isFocused = HasEditorFocus();
            m_editorLayer.totalTime = (float)now;
            m_authoring.Update();
            m_editSession.Tick((float)now, delta);
            using (m_editSession.EnterExecutionScope())
            {
                m_layers.Update(delta);
                m_layers.LateUpdate(delta);
                m_layers.RenderFrame(delta);
            }

            if (m_frameCount == 0)
                BootLog("First editor frame completed.");

            m_hasRenderedFrame = true;
            m_frameCount++;
            if (smokeFrameLimit.HasValue && m_frameCount >= smokeFrameLimit.Value)
            {
                BootLog($"Smoke frame limit reached after {m_frameCount} frame(s).");
                m_running = false;
            }
        }

        SaveBeforeShutdown();
        BootLog("Run loop end.");
        return 0;
    }

    /// <summary>
    /// Releases the resources owned by this implementation.
    /// </summary>
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
