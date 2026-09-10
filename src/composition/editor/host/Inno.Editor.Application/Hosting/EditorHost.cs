using Inno.Engine.Default;
using Inno.Runtime.Contracts;
using System;
using Inno.Core.Diagnostics;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Inno.Adapter;
using Inno.Adapter.Audio;
using Inno.Adapter.Presentation;
using Inno.Assets;
using Inno.Assets.Pipeline;
using Inno.Animation.Runtime;
using Inno.Audio.Runtime;
using Inno.Audio;
using Inno.Build;
using Inno.Build.Platform.MacOS;
using Inno.Build.Platform.Windows;
using Inno.Core.Events;
using Inno.Core.Layers;
using Inno.Core.Logging;
using Inno.Core.Settings;
using Inno.Editor.Audio;
using Inno.Editor.Core;
using Inno.Editor.Rendering;
using Inno.Input.Runtime;
using Inno.Platform;
using Inno.Rendering;
using Inno.Rendering.Assets;
using Inno.Rendering.Runtime;
using Inno.Runtime;
using Inno.Scene;
using Inno.Shell;
using Inno.Storage.Runtime;
using ShellHost = Inno.Shell.Shell;

namespace Inno.Editor.Application;

/// <summary>
/// Editor product host built on the common backend-neutral composition shell.
/// </summary>
internal sealed class EditorHost : ShellHost
{
    private const string C_LOG_DIRECTORY_NAME = "Logs";
    private const string C_BOOT_LOG_FILE_NAME = "EditorBoot.log";

    private readonly IAuthoringAdapterCatalog m_authoringAdapters;
    private readonly HashSet<uint> m_focusedWindowIds = [];
    private readonly PresentationBackend m_presentationBackend;
    private readonly EditorHostResourceStack m_resources;
    private readonly string m_bootLogPath;
    private RuntimeSession? m_editSession;
    private IEditorAudioHost? m_audio;
    private EditorAuthoringServices? m_authoring;
    private RenderRuntime? m_rendering;
    private LayerStack? m_layers;
    private EditorLayer? m_editorLayer;
    private bool m_shutdownStateSaved;

    private EditorHost(
        IAuthoringAdapterCatalog adapterCatalog,
        AdapterSelection adapterSelection,
        PresentationBackend presentationBackend,
        string projectDirectory,
        string bootLogPath,
        GraphicsApi? preferredGraphicsApi)
        : base(
            adapterCatalog,
            new ShellOptions
            {
                adapters = adapterSelection,
                window = new PlatformWindowOptions
                {
                    title = "Inno Editor",
                    width = 1600,
                    height = 900,
                    resizable = true,
                    highPixelDensity = true
                },
                preferredGraphicsApi = preferredGraphicsApi,
                verticalSync = false,
                sRgbBackbuffer = true
            })
    {
        m_authoringAdapters = adapterCatalog;
        m_presentationBackend = presentationBackend;
        this.projectDirectory = projectDirectory;
        m_bootLogPath = bootLogPath;
        m_resources = new EditorHostResourceStack(
            exception => AppendBootLog(bootLogPath, $"Teardown failure: {exception}"));
    }

    /// <summary>
    /// Gets the normalized project directory owned by this host.
    /// </summary>
    public string projectDirectory { get; }

    internal static EditorHost Create(
        IAuthoringAdapterCatalog adapterCatalog,
        AdapterSelection adapterSelection,
        PresentationBackend presentationBackend,
        string projectDirectory,
        GraphicsApi? preferredGraphicsApi = null)
    {
        ArgumentNullException.ThrowIfNull(adapterCatalog);
        string normalizedProject = PrepareProjectDirectory(projectDirectory);
        string logDirectory = Path.Combine(normalizedProject, C_LOG_DIRECTORY_NAME);
        Directory.CreateDirectory(logDirectory);
        string bootLogPath = Path.Combine(logDirectory, C_BOOT_LOG_FILE_NAME);
        var host = new EditorHost(
            adapterCatalog,
            adapterSelection,
            presentationBackend,
            normalizedProject,
            bootLogPath,
            preferredGraphicsApi);
        try
        {
            host.Initialize();
            return host;
        }
        catch
        {
            host.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Records that the shared composition run loop is starting.
    /// </summary>
    protected override void OnStarting()
        => BootLog("Run loop start.");

    /// <summary>
    /// Applies editor focus policy before accepting an orderly application or primary-window exit.
    /// </summary>
    /// <param name="evnt">
    /// Backend-neutral event produced by the common shell.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the editor should stop after processing the event.
    /// </returns>
    protected override bool ShouldExit(Event evnt)
    {
        UpdateFocusedWindows(evnt);
        if (evnt is ApplicationQuitEvent)
        {
            if (!hasCompletedFrame)
            {
                BootLog("Ignored early ApplicationQuitEvent before first frame.");
                return false;
            }
            BootLog($"Exit requested by event: {evnt.GetType().Name}.");
            return true;
        }

        bool shouldExit = evnt is WindowCloseEvent closeEvent
                          && closeEvent.windowId == primaryWindow.windowId;
        if (shouldExit)
            BootLog($"Exit requested by event: {evnt.GetType().Name}.");
        return shouldExit;
    }

    /// <summary>
    /// Routes one backend-neutral platform event into the active editor runtime session.
    /// </summary>
    /// <param name="evnt">
    /// Event produced by the common shell.
    /// </param>
    protected override void OnEvent(Event evnt)
        => editSession.events.Enqueue(evnt);

    /// <summary>
    /// Advances authoring services, the edit runtime session, and editor presentation for one frame.
    /// </summary>
    /// <param name="frame">
    /// Immutable timing and identity for the current shell frame.
    /// </param>
    protected override void OnFrame(ShellFrame frame)
    {
        if (frame.frameIndex == 0)
            BootLog("About to execute first editor frame.");

        using (rendering.EnterExecutionScope())
        using (audio.EnterExecutionScope(editSession))
        {
            editorLayer.isFocused = HasEditorFocus();
            editorLayer.totalTime = (float)frame.totalTime;
            authoring.Update();
            editSession.Tick(frame.deltaTime);
            using (editSession.EnterExecutionScope())
            {
                layers.OnUpdate(frame.deltaTime);
                layers.OnLateUpdate(frame.deltaTime);
            }
        }

        if (frame.frameIndex == 0)
            BootLog("First editor frame completed.");
    }

    /// <summary>
    /// Records successful completion of a bounded native smoke run.
    /// </summary>
    /// <param name="frameCount">
    /// Number of editor frames completed.
    /// </param>
    protected override void OnSmokeCompleted(int frameCount)
        => BootLog($"Smoke frame limit reached after {frameCount} frame(s).");
    /// <summary>
    /// Submits product UI requests while the host output pipeline is open.
    /// </summary>
    /// <param name="frame">
    /// The current application frame.
    /// </param>
    protected override void OnPresentation(ShellFrame frame)
    {
        using IDisposable scope = editSession.EnterExecutionScope();
        layers.RenderFrame(frame.deltaTime);
    }

    /// <summary>
    /// Saves product-owned state before common adapter resources begin shutdown.
    /// </summary>
    protected override void OnStopping()
    {
        SaveBeforeShutdown();
        BootLog("Run loop end.");
    }

    /// <summary>
    /// Releases editor-owned resources before the common shell destroys shared adapters.
    /// </summary>
    protected override void DisposeProductResources()
    {
        BootLog("Dispose start.");
        m_editorLayer?.QuiescePlayMode();
        m_resources.Dispose();
        m_editorLayer = null;
        m_layers = null;
        m_rendering = null;
        m_audio = null;
        m_editSession = null;
        m_authoring = null;
        BootLog("Dispose end.");
    }

    private RuntimeSession editSession
        => m_editSession ?? throw new InvalidOperationException("The Editor runtime session is not initialized.");

    private IEditorAudioHost audio
        => m_audio ?? throw new InvalidOperationException("The Editor audio host is not initialized.");

    private EditorAuthoringServices authoring
        => m_authoring ?? throw new InvalidOperationException("The Editor authoring services are not initialized.");

    private RenderRuntime rendering
        => m_rendering ?? throw new InvalidOperationException("The Editor rendering runtime is not initialized.");

    private LayerStack layers
        => m_layers ?? throw new InvalidOperationException("The Editor layer stack is not initialized.");

    private EditorLayer editorLayer
        => m_editorLayer ?? throw new InvalidOperationException("The Editor layer is not initialized.");

    private void Initialize()
    {
        bool overlayPushed = false;
        IPresentationContext? presentation = null;

        AppendBootLog(m_bootLogPath, "EditorHost creation start.");
        InitializeAdapterResources();
        if (primaryWindow.isFocused)
            m_focusedWindowIds.Add(primaryWindow.windowId);

        EngineHost engineHost = m_resources.Acquire(
            () => new EngineHostBuilder()
                .UseMetadataCache(Path.Combine(projectDirectory, "Library", "Assemblies"))
                .Build(),
            static host => host.Dispose());
        var consoleLog = new ConsoleLogSink();
        engineHost.logs.RegisterSink(consoleLog);
        m_resources.Register(() => engineHost.logs.UnregisterSink(consoleLog));
        EditorAuthoringServices activeAuthoring = m_resources.Acquire(
            () => EditorAuthoringServices.Start(projectDirectory, engineHost),
            static services => services.Dispose());
        m_authoring = activeAuthoring;
        _ = m_resources.Acquire(
            () => AssetExecutionContext.EnterScope(activeAuthoring.assets),
            static scope => scope.Dispose());
        _ = m_resources.Acquire(
            () => ProjectSettingsExecutionContext.EnterScope(activeAuthoring.settings),
            static scope => scope.Dispose());
        EditorAudioHost activeAudio = m_resources.Acquire(
            () => new EditorAudioHost(
                engineHost.types,
                activeAuthoring.assets,
                engineHost.diagnostics,
                () => adapters.audio.CreateDevice(adapterSelection.audio, new AudioBackendOptions()),
                () => activeAuthoring.settings.TryGet(
                        AudioProjectSettings.settingId,
                        out AudioProjectSettings? configured) && configured is not null
                    ? configured
                    : new AudioProjectSettings()),
            static host => host.Dispose());
        m_audio = activeAudio;
        RuntimeSession activeEditSession = m_resources.Acquire(
            () => engineHost.CreateSession(new RuntimeSessionOptions
            {
                kind = RuntimeSessionKind.Edit,
                applicationId = "inno.editor",
                persistentDataDirectory = Path.Combine(
                    projectDirectory,
                    "Library",
                    "PersistentData",
                    "inno.editor"),
                fixedDeltaTime = 1f / 60f,
                maxFrameDeltaTime = 0.25f,
                maxFixedStepsPerFrame = 8,
                jobExecutionMode = RuntimeJobExecutionMode.WorkerPool,
                createSubsystems = owner => CreateStandardRuntimeSubsystems(activeAudio, owner),
                referenceResolvers = [activeAuthoring.assets]
            }),
            static session => session.Dispose());
        m_editSession = activeEditSession;
        _ = m_resources.Acquire(
            activeEditSession.EnterExecutionScope,
            static scope => scope.Dispose());

        var buildPipeline = new BuildPipeline(
            activeAuthoring.assets,
            activeAuthoring.plugins,
            activeAuthoring.settings,
            engineHost.serialization,
            engineHost.generations,
            activeAuthoring.compiler,
            ResolveSupportPackRoot(),
            [
                new MacOSArm64GameBuildTarget(activeAuthoring.assets, engineHost.serialization),
                new WindowsX64GameBuildTarget(activeAuthoring.assets, engineHost.serialization)
            ]);
        BuildSettings defaultBuildSettings = BuildSettings.CreateDefault(
            Path.GetFileName(Path.TrimEndingDirectorySeparator(projectDirectory)),
            FindDefaultStartupScene(activeAuthoring.assets),
            buildPipeline.defaultGameTarget);
        var buildSettings = new BuildSettingsStore(
            Path.Combine(projectDirectory, SettingsFileNames.build),
            engineHost.serialization,
            defaultBuildSettings);
        LayerStack activeLayers = m_resources.Acquire(
            () => new LayerStack(() => activeEditSession.events.CreateHub()),
            static stack => stack.Dispose());
        m_layers = activeLayers;
        m_resources.Register(() =>
        {
            renderDevice.BeginFrame();
            try
            {
                presentation?.PrepareFrame(ulong.MaxValue);
            }
            finally
            {
                _ = renderDevice.EndFrame();
            }
        });

        var shaderCompiler = new ShaderCompiler(
            m_authoringAdapters.renderingAuthoring.CreateShaderCompilerToolchain(
                adapterSelection.rendering));
        ITextureTargetCompiler textureCompiler =
            m_authoringAdapters.renderingAuthoring.CreateTextureTargetCompiler(
                adapterSelection.rendering);
        DiagnosticReporter renderDiagnostics = m_resources.Acquire(
            () => engineHost.diagnostics.CreateReporter(new DiagnosticSource("inno.editor.rendering", "Rendering")),
            static sink => sink.Dispose());
        var renderArtifacts = m_resources.Acquire(
            () => new EditorRenderTargetArtifactProvider(
                activeAuthoring.assets,
                engineHost.serialization,
                shaderCompiler,
                textureCompiler,
                renderDiagnostics),
            static provider => provider.Dispose());
        presentation = m_resources.Acquire(
            () => m_authoringAdapters.presentation.CreateContext(
                m_presentationBackend,
                new PresentationBackendOptions
                {
                    platformApplication = platformApplication,
                    window = primaryWindow,
                    renderDevice = renderDevice,
                    shaderCompiler = shaderCompiler,
                    assetSourceDirectory = Path.Combine(projectDirectory, "Assets"),
                    features = PresentationFeatures.MultipleWindows
                               | PresentationFeatures.Docking
                               | PresentationFeatures.SmoothResize
                }),
            static context => context.Dispose());
        var renderingLayer = new RenderRuntime(
            engineHost.types,
            renderDevice,
            renderDiagnostics,
            contributors: [presentation],
            targetArtifacts: renderArtifacts);
        m_rendering = renderingLayer;
        RuntimeSubsystemPipeline hostPipeline = engineHost.CreateHostPipeline(DefaultEngine.CreateHostSubsystems(renderingLayer));
        m_resources.Register(hostPipeline.Dispose);
        UseHostPipeline(hostPipeline);
        var reloadCoordinator = new EditorReloadCoordinator();
        var renderingHost = m_resources.Acquire(
            () => new EditorRenderingHostService(
                renderingLayer,
                presentation,
                reloadCoordinator),
            static service => service.Dispose());
        var editorContext = new EditorContext(projectDirectory);
        presentation.SetLayoutFile(null);
        presentation.LoadLayout(editorContext.imguiLayout);
        var playOptions = new RuntimeSessionOptions
        {
            kind = RuntimeSessionKind.Play,
            applicationId = "inno.editor.play",
            persistentDataDirectory = Path.Combine(
                projectDirectory,
                "Library",
                "PersistentData",
                "inno.editor.play"),
            fixedDeltaTime = 1f / 60f,
            maxFrameDeltaTime = 0.25f,
            maxFixedStepsPerFrame = 8,
            jobExecutionMode = RuntimeJobExecutionMode.WorkerPool,
            createSubsystems = owner => CreateStandardRuntimeSubsystems(activeAudio, owner),
            referenceResolvers = [activeAuthoring.assets]
        };
        EditorLayer layer = new EditorLayer(
            presentation,
            editorContext,
            engineHost.types,
            engineHost.logs,
            [
                renderingHost,
                framePacing,
                reloadCoordinator,
                engineHost,
                engineHost.modules,
                engineHost.diagnostics,
                activeEditSession,
                playOptions,
                activeAuthoring.assets,
                activeAuthoring.plugins,
                activeAuthoring.settings,
                activeAuthoring.compiler,
                buildPipeline,
                buildSettings,
                engineHost.types,
                engineHost.serialization,
                activeAuthoring.identities,
                activeEditSession.identities,
                activeAudio
            ])
        {
            isFocused = primaryWindow.isFocused
        };
        m_editorLayer = layer;
        m_resources.Register(() =>
        {
            if (overlayPushed)
                activeLayers.PopOverlay(layer);
            else
                layer.DisposeUnattached();
        });
        activeLayers.PushOverlay(layer);
        overlayPushed = true;
        if (layer.panelCount == 0)
            throw new InvalidOperationException("No editor panels were discovered from the active host assemblies.");

        BootLog($"Editor layer attached with {layer.panelCount} panel(s).");
        BootLog(
            $"Rendering initialized with {renderDevice.capabilities.backend} "
            + $"(views={renderDevice.capabilities.limits.maxViews}).");
        BootLog(
            $"AssetPipeline initialized={activeAuthoring.assets.isInitialized} "
            + $"root='{activeAuthoring.assets.assetRoot}'.");
    }

    private IReadOnlyList<IRuntimeSubsystemFactory> CreateStandardRuntimeSubsystems(IEditorAudioHost activeAudio, RuntimeSession owner)
    {
        ArgumentNullException.ThrowIfNull(activeAudio);
        return DefaultEngine.CreateSessionSubsystems(new EngineSessionComposition(
            owner, adapters, adapterSelection, inputSource, authoring.assets,
            () => authoring.settings.TryGet(AudioProjectSettings.settingId, out AudioProjectSettings? settings) && settings is not null
                ? settings : new AudioProjectSettings(),
            activeAudio.CreateRuntimeSubsystemFactory(owner)));
    }

    private static string ResolveSupportPackRoot()
    {
        string? configured = Environment.GetEnvironmentVariable("INNO_SUPPORT_PACK_ROOT");
        return string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(AppContext.BaseDirectory, "SupportPacks")
            : Path.GetFullPath(configured);
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

    private static string FindDefaultStartupScene(AssetPipeline assets)
    {
        foreach (AssetFileEntry entry in assets.GetFileSystemEntries(includeDirectories: false)
                     .Where(static entry => entry.source == AssetSourceId.project)
                     .Where(static entry => !AssetSample.IsRuntimeExcluded(entry.assetPath, isDirectory: false))
                     .OrderBy(static entry => entry.assetPath.localPath, StringComparer.Ordinal))
        {
            if (assets.TryGetAssetType(entry.assetPath, out Type? type) && type == typeof(SceneAsset))
                return entry.assetPath.ToString();
        }
        return string.Empty;
    }

    private bool HasEditorFocus()
        => m_focusedWindowIds.Count > 0;

    private void SaveBeforeShutdown()
    {
        if (m_shutdownStateSaved || m_editorLayer is null)
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
