using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Inno.Assets;
using Inno.Audio;
using Inno.Audio.MiniAudio;
using Inno.Audio.Runtime;
using Inno.Audio.Scene;
using Inno.Core.Events;
using Inno.Core.Logging;
using Inno.Core.Serialization;
using Inno.Core.Settings;
using Inno.Extensibility.Modules;
using Inno.Runtime;
using Inno.Scene;
using Inno.Platform;
using Inno.Platform.Sdl3;
using Inno.Rendering;
using Inno.Rendering.Bgfx;
using Inno.Rendering.Runtime;
using Inno.Rendering.Scene;

namespace Inno.Player;

internal sealed class GamePlayerHost : IDisposable
{
    private readonly Sdl3PlatformApplication m_platform;
    private readonly Sdl3PlatformWindow m_window;
    private readonly BgfxDevice m_device;
    private readonly AudioRuntimeLayer m_audio;
    private readonly EngineHost m_engine;
    private readonly ProjectSettingsStore m_settings;
    private readonly RuntimeSession m_session;
    private readonly RenderRuntimeLayer m_rendering;
    private bool m_disposed;

    private GamePlayerHost(
        Sdl3PlatformApplication platform,
        Sdl3PlatformWindow window,
        BgfxDevice device,
        AudioRuntimeLayer audio,
        EngineHost engine,
        ProjectSettingsStore settings,
        RuntimeSession session,
        RenderRuntimeLayer rendering)
    {
        m_platform = platform;
        m_window = window;
        m_device = device;
        m_audio = audio;
        m_engine = engine;
        m_settings = settings;
        m_session = session;
        m_rendering = rendering;
    }

    internal static GamePlayerHost Create()
    {
        _ = typeof(AudioProjectSettings);
        _ = typeof(AudioSource);
        string packagedContentRoot = ResolvePackagedContentRoot();
        byte[] manifestEnvelope = File.ReadAllBytes(Path.Combine(packagedContentRoot, "runtime.manifest"));
        string applicationId = RuntimeManifestEnvelope.ReadApplicationId(manifestEnvelope);
        string persistentRoot = ResolvePersistentRoot(applicationId);
        string runtimeContentRoot = RuntimeContentDeployment.Materialize(
            packagedContentRoot,
            persistentRoot);
        EngineHost engine = new EngineHostBuilder()
            .UseMetadataCache(Path.Combine(persistentRoot, "Library", "RuntimeMetadata"))
            .Build();
        ProjectSettingsStore? settings = null;
        try
        {
            using SerializationGeneration serialization = engine.serialization.CaptureGeneration();
            GameRuntimeManifest manifest = RuntimeManifestEnvelope.Decode(manifestEnvelope, serialization);
            ActivateRuntimeModules(
                engine.modules,
                manifest.modules,
                Path.Combine(runtimeContentRoot, "Managed"));
            settings = new ProjectSettingsStore(
                Path.Combine(runtimeContentRoot, SettingsFileNames.project),
                engine.types,
                engine.serialization,
                new ProjectId(applicationId));
            settings.SetContributors(manifest.CreateSettingContributors());
            settings.RebuildCurrent();
            if (!string.Equals(settings.projectId.value, applicationId, StringComparison.Ordinal))
                throw new InvalidDataException("Runtime manifest and Project Settings identities do not match.");

            GamePresentationSettings presentation = settings.Get<GamePresentationSettings>(
                GamePresentationSettings.settingId);
            RuntimeSession session = engine.CreateSession(new RuntimeSessionOptions
            {
                kind = RuntimeSessionKind.Player,
                applicationId = manifest.applicationId,
                runtimeContentDirectory = runtimeContentRoot,
                persistentDataDirectory = persistentRoot
            });

            var platform = new Sdl3PlatformApplication();
            try
            {
                Sdl3PlatformWindow window = platform.CreateWindow(new PlatformWindowOptions
                {
                    title = manifest.productName,
                    width = manifest.windowWidth,
                    height = manifest.windowHeight,
                    resizable = true,
                    highPixelDensity = true
                });
                try
                {
                    var device = new BgfxDevice(new BgfxDeviceOptions
                    {
                        window = window,
                        verticalSync = true,
                        sRgbBackbuffer = true
                    });
                    try
                    {
                        var diagnostics = new PlayerRenderDiagnosticSink(engine.logs);
                        var rendering = new RenderRuntimeLayer(
                            engine.types,
                            device,
                            diagnostics,
                            targetArtifacts: new FileRenderTargetArtifactProvider(runtimeContentRoot),
                            contentScopeProvider: () => SceneRenderContent.CreateScope(session.scenes),
                            primaryPresentationViewportProvider: size => CreatePresentationViewport(
                                presentation,
                                size));
                        AudioRuntimeLayer? audio = null;
                        bool renderingAttached = false;
                        try
                        {
                            audio = CreateAudioRuntime(engine, session, settings);
                            using (settings.EnterExecutionScope())
                            using (audio.EnterExecutionScope())
                            using (session.EnterExecutionScope())
                            {
                                rendering.OnAttach();
                                renderingAttached = true;
                                SceneAsset startupAsset = session.assets.Load<SceneAsset>(
                                    AssetPath.Parse(manifest.startupScene));
                                session.scenes.LoadScene(startupAsset.Instantiate(
                                    engine.serialization,
                                    session.assets));
                            }
                            return new GamePlayerHost(
                                platform,
                                window,
                                device,
                                audio,
                                engine,
                                settings,
                                session,
                                rendering);
                        }
                        catch
                        {
                            audio?.Dispose();
                            if (renderingAttached)
                            {
                                using (settings.EnterExecutionScope())
                                using (session.EnterExecutionScope())
                                    rendering.OnDetach();
                            }
                            throw;
                        }
                    }
                    catch
                    {
                        device.Dispose();
                        throw;
                    }
                }
                catch
                {
                    window.Dispose();
                    throw;
                }
            }
            catch
            {
                platform.Dispose();
                throw;
            }
        }
        catch
        {
            settings?.Dispose();
            engine.Dispose();
            throw;
        }
    }

    private static RenderViewport CreatePresentationViewport(
        GamePresentationSettings presentation,
        RenderPresentationSize size)
    {
        GamePresentationViewport viewport = presentation.CalculateViewport(size.width, size.height);
        return new RenderViewport(viewport.x, viewport.y, viewport.width, viewport.height);
    }

    internal int Run(int? smokeFrameLimit = null)
    {
        if (smokeFrameLimit is <= 0)
            throw new ArgumentOutOfRangeException(nameof(smokeFrameLimit));
        Stopwatch timer = Stopwatch.StartNew();
        double previous = 0d;
        bool running = true;
        int renderedFrameCount = 0;
        while (running && !m_window.isClosed)
        {
            while (m_platform.PollEvent(out Event? evnt))
            {
                if (evnt is null)
                    continue;
                if (evnt is ApplicationQuitEvent
                    || evnt is WindowCloseEvent close && close.windowId == m_window.windowId)
                {
                    running = false;
                    break;
                }
                if (evnt is WindowResizeEvent resize && resize.windowId == m_window.windowId)
                    m_device.ResizeBackbuffer(m_window.pixelWidth, m_window.pixelHeight);
                m_session.events.Enqueue(evnt);
            }
            double now = timer.Elapsed.TotalSeconds;
            float delta = Math.Max(0f, (float)(now - previous));
            using (m_settings.EnterExecutionScope())
            using (m_rendering.EnterExecutionScope())
            using (m_audio.EnterExecutionScope())
            {
                m_session.Tick((float)now, delta);
                using (m_session.EnterExecutionScope())
                {
                    m_audio.Update(delta);
                    m_rendering.OnBeforeRender(delta);
                    try
                    {
                        m_rendering.OnRender(delta);
                    }
                    finally
                    {
                        m_rendering.OnAfterRender(delta);
                    }
                }
            }
            previous = now;
            renderedFrameCount++;
            if (renderedFrameCount >= smokeFrameLimit)
                running = false;
        }
        if (smokeFrameLimit.HasValue)
        {
            RenderFrameStatistics? statistics;
            using (m_rendering.EnterExecutionScope())
                statistics = GraphicsSettings.frameStatistics;
            Console.WriteLine(
                $"INNO-SMOKE frames={renderedFrameCount} "
                + $"views={statistics?.viewCount ?? 0} "
                + $"draws={statistics?.drawCount ?? 0} "
                + $"dispatches={statistics?.dispatchCount ?? 0}");
        }
        return 0;
    }

    /// <summary>
    /// Releases the resources owned by this instance.
    /// </summary>
    public void Dispose()
    {
        if (m_disposed)
            return;
        m_disposed = true;
        using (m_settings.EnterExecutionScope())
        using (m_rendering.EnterExecutionScope())
        using (m_audio.EnterExecutionScope())
        using (m_session.EnterExecutionScope())
            m_rendering.OnDetach();
        m_audio.Dispose();
        m_session.Dispose();
        m_settings.Dispose();
        m_engine.Dispose();
        m_device.Dispose();
        m_window.Dispose();
        m_platform.Dispose();
    }

    private static string ResolvePackagedContentRoot()
    {
        string besideExecutable = Path.Combine(AppContext.BaseDirectory, "Content");
        string macResources = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "Resources", "Content"));
        string result = Directory.Exists(macResources) ? macResources : besideExecutable;
        if (!Directory.Exists(result))
            throw new DirectoryNotFoundException($"Game content root '{result}' does not exist.");
        return result;
    }

    private static AudioRuntimeLayer CreateAudioRuntime(
        EngineHost engine,
        RuntimeSession session,
        ProjectSettingsStore settings)
    {
        var diagnostics = new PlayerAudioDiagnosticSink(engine.logs);
        IAudioDevice device;
        try
        {
            device = new MiniAudioDevice();
        }
        catch (Exception exception)
        {
            diagnostics.Publish(new AudioDiagnostic(
                "AUDIO_DEVICE_INIT_FAILED",
                $"The operating-system audio device could not start; the session is explicitly muted: {exception.Message}",
                AudioDiagnosticSeverity.Warning,
                "MiniAudio"));
            device = new MutedAudioDevice();
        }

        AudioProjectSettings audioSettings = settings.TryGet(
                AudioProjectSettings.settingId,
                out AudioProjectSettings? configured) && configured is not null
            ? configured
            : new AudioProjectSettings();
        var runtime = new AudioRuntimeLayer(
            engine.types,
            device,
            session.assets,
            session.events,
            diagnostics,
            new AudioRuntimeOptions
            {
                maxVoices = audioSettings.maxVoices,
                decodedCacheBudgetBytes = audioSettings.decodedCacheBudgetBytes,
                automaticStreamingThresholdBytes = audioSettings.automaticStreamingThresholdBytes
            },
            new SceneAudioContent(session.scenes).Capture,
            static () => new MiniAudioDevice());
        try
        {
            if (audioSettings.defaultMixer is not null && !runtime.ApplyMixer(audioSettings.defaultMixer))
                throw new InvalidOperationException("The configured default audio mixer could not be activated.");
            if (!runtime.SetBusVolume(AudioBusId.master, audioSettings.masterVolume))
                throw new InvalidOperationException("The configured master audio volume could not be applied.");
            return runtime;
        }
        catch
        {
            runtime.Dispose();
            throw;
        }
    }

    private static string ResolvePersistentRoot(string applicationId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationId);
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "InnoEngine",
            applicationId);
    }

    private static void ActivateRuntimeModules(
        ModuleHost modules,
        IReadOnlyList<GameRuntimeModule> deployedModules,
        string managedRoot)
    {
        ArgumentNullException.ThrowIfNull(modules);
        ArgumentNullException.ThrowIfNull(deployedModules);
        string root = Path.GetFullPath(managedRoot);
        if (!Directory.Exists(root))
            throw new DirectoryNotFoundException($"Deployed managed content root '{root}' does not exist.");

        string[] declaredFiles = deployedModules
            .SelectMany(static module => module.preloadAssemblies.Prepend(module.mainAssembly))
            .ToArray();
        string[] actualFiles = Directory.EnumerateFiles(root, "*.dll", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray()!;
        if (!declaredFiles.Order(StringComparer.OrdinalIgnoreCase).SequenceEqual(
                actualFiles,
                StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "Deployed managed assemblies do not exactly match the frozen runtime module manifest.");
        }

        AssemblyLoadRequest[] requests = deployedModules.Select(module => new AssemblyLoadRequest
        {
            moduleName = module.name,
            mainAssemblyPath = Path.Combine(root, module.mainAssembly),
            preloadAssemblyPaths = module.preloadAssemblies
                .Select(fileName => Path.Combine(root, fileName))
                .ToArray(),
            upstreamModuleNames = module.dependencies,
            collectible = true,
            domain = module.domain,
            scope = AssemblyScope.Runtime
        }).ToArray();
        using AssemblyReloadSession activation = modules.BeginReload(requests);
        activation.Activate();
        _ = activation.Complete();

        string[] activeNames = modules.modules
            .Select(static module => module.moduleName)
            .Order(StringComparer.Ordinal)
            .ToArray();
        string[] expectedNames = deployedModules
            .Select(static module => module.name)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (!activeNames.SequenceEqual(expectedNames, StringComparer.Ordinal))
            throw new InvalidOperationException("The frozen runtime module generation was not activated completely.");
    }

    private sealed class PlayerRenderDiagnosticSink : IRenderDiagnosticSink
    {
        private readonly HashSet<DiagnosticIdentity> m_active = [];
        private readonly Logger m_logger;

        internal PlayerRenderDiagnosticSink(LogRouter logs)
        {
            ArgumentNullException.ThrowIfNull(logs);
            m_logger = logs.CreateLogger<PlayerRenderDiagnosticSink>();
        }

        /// <summary>
        /// Publishes the supplied diagnostic to the configured observers.
        /// </summary>
        /// <param name="diagnostic">
        /// The diagnostic consumed by publish; ownership remains with the caller unless explicitly stated otherwise.
        /// </param>
        public void Publish(RenderDiagnostic diagnostic)
        {
            ArgumentNullException.ThrowIfNull(diagnostic);
            var identity = new DiagnosticIdentity(
                diagnostic.code,
                diagnostic.sourceId,
                diagnostic.message,
                diagnostic.severity);
            if (!m_active.Add(identity))
                return;
            LogLevel level = diagnostic.severity switch
            {
                RenderDiagnosticSeverity.Error => LogLevel.Error,
                RenderDiagnosticSeverity.Warning => LogLevel.Warn,
                _ => LogLevel.Info
            };
            m_logger.Write(level, "[{0}] {1}", [diagnostic.code, diagnostic.message]);
        }

        /// <summary>
        /// Retires a current rendering diagnostic so a later recurrence can be logged again.
        /// </summary>
        /// <param name="code">
        /// The stable machine-readable code of the resolved diagnostic.
        /// </param>
        /// <param name="sourceId">
        /// The same optional source identity used when the diagnostic was published.
        /// </param>
        /// <exception cref="ArgumentException">
        /// Thrown when <paramref name="code"/> is empty or contains only whitespace.
        /// </exception>
        public void Resolve(string code, string? sourceId = null)
        {
            if (string.IsNullOrWhiteSpace(code))
                throw new ArgumentException("A rendering diagnostic code is required.", nameof(code));
            m_active.RemoveWhere(identity =>
                string.Equals(identity.code, code, StringComparison.Ordinal) &&
                string.Equals(identity.sourceId, sourceId, StringComparison.Ordinal));
        }

        private readonly record struct DiagnosticIdentity(
            string code,
            string? sourceId,
            string message,
            RenderDiagnosticSeverity severity);
    }

    private sealed class PlayerAudioDiagnosticSink : IAudioDiagnosticSink
    {
        private readonly HashSet<AudioDiagnosticIdentity> m_active = [];
        private readonly Logger m_logger;

        internal PlayerAudioDiagnosticSink(LogRouter logs)
        {
            ArgumentNullException.ThrowIfNull(logs);
            m_logger = logs.CreateLogger<PlayerAudioDiagnosticSink>();
        }

        /// <summary>
        /// Writes a new structured audio diagnostic to the Player log once per identity.
        /// </summary>
        /// <param name="diagnostic">
        /// Diagnostic emitted by the active audio runtime.
        /// </param>
        public void Publish(AudioDiagnostic diagnostic)
        {
            ArgumentNullException.ThrowIfNull(diagnostic);
            var identity = new AudioDiagnosticIdentity(
                diagnostic.code,
                diagnostic.source,
                diagnostic.message,
                diagnostic.severity);
            if (!m_active.Add(identity))
                return;
            LogLevel level = diagnostic.severity switch
            {
                AudioDiagnosticSeverity.Error => LogLevel.Error,
                AudioDiagnosticSeverity.Warning => LogLevel.Warn,
                _ => LogLevel.Info
            };
            m_logger.Write(level, "[{0}] {1}", [diagnostic.code, diagnostic.message]);
        }

        private readonly record struct AudioDiagnosticIdentity(
            string code,
            string? source,
            string message,
            AudioDiagnosticSeverity severity);
    }
}
