using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using Inno.Assets;
using Inno.Core.Events;
using Inno.Core.Logging;
using Inno.Core.Serialization;
using Inno.Core.Settings;
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
    private readonly EngineHost m_engine;
    private readonly ProjectSettingsStore m_settings;
    private readonly RuntimeSession m_session;
    private readonly RenderRuntimeLayer m_rendering;
    private bool m_disposed;

    private GamePlayerHost(
        Sdl3PlatformApplication platform,
        Sdl3PlatformWindow window,
        BgfxDevice device,
        EngineHost engine,
        ProjectSettingsStore settings,
        RuntimeSession session,
        RenderRuntimeLayer rendering)
    {
        m_platform = platform;
        m_window = window;
        m_device = device;
        m_engine = engine;
        m_settings = settings;
        m_session = session;
        m_rendering = rendering;
    }

    internal static GamePlayerHost Create()
    {
        string packagedContentRoot = ResolvePackagedContentRoot();
        byte[] manifestEnvelope = File.ReadAllBytes(Path.Combine(packagedContentRoot, "runtime.manifest"));
        string applicationId = RuntimeManifestEnvelope.ReadApplicationId(manifestEnvelope);
        string persistentRoot = ResolvePersistentRoot(applicationId);
        string runtimeContentRoot = RuntimeContentDeployment.Materialize(
            packagedContentRoot,
            persistentRoot);
        LoadRuntimeAssemblies(Path.Combine(runtimeContentRoot, "Managed"));
        EngineHost engine = new EngineHostBuilder()
            .UseMetadataCache(Path.Combine(persistentRoot, "Library", "RuntimeMetadata"))
            .Build();
        ProjectSettingsStore? settings = null;
        try
        {
            using SerializationGeneration serialization = engine.serialization.CaptureGeneration();
            GameRuntimeManifest manifest = RuntimeManifestEnvelope.Decode(manifestEnvelope, serialization);
            settings = new ProjectSettingsStore(
                Path.Combine(runtimeContentRoot, "ProjectSettings.inno"),
                engine.types,
                engine.serialization);
            settings.SetContributors(manifest.CreateSettingContributors());
            settings.RebuildCurrent();
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
                            contentScopeProvider: () => SceneRenderContent.CreateScope(session.scenes));
                        using (settings.EnterExecutionScope())
                        using (session.EnterExecutionScope())
                        {
                            rendering.OnAttach();
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
                            engine,
                            settings,
                            session,
                            rendering);
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
            {
                m_session.Tick((float)now, delta);
                using (m_session.EnterExecutionScope())
                {
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
        using (m_session.EnterExecutionScope())
            m_rendering.OnDetach();
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

    private static string ResolvePersistentRoot(string applicationId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationId);
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "InnoEngine",
            applicationId);
    }

    private static void LoadRuntimeAssemblies(string managedRoot)
    {
        if (!Directory.Exists(managedRoot))
            return;
        Dictionary<string, string> assemblies = Directory
            .EnumerateFiles(managedRoot, "*.dll", SearchOption.TopDirectoryOnly)
            .ToDictionary(
                static path => AssemblyName.GetAssemblyName(path).Name!,
                static path => Path.GetFullPath(path),
                StringComparer.OrdinalIgnoreCase);
        AssemblyLoadContext.Default.Resolving += (_, name) =>
            name.Name is not null && assemblies.TryGetValue(name.Name, out string? path)
                ? AssemblyLoadContext.Default.LoadFromAssemblyPath(path)
                : null;
        foreach (string path in assemblies.Values.Order(StringComparer.Ordinal))
        {
            string name = AssemblyName.GetAssemblyName(path).Name!;
            if (AppDomain.CurrentDomain.GetAssemblies().Any(assembly =>
                    string.Equals(assembly.GetName().Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }
            _ = AssemblyLoadContext.Default.LoadFromAssemblyPath(path);
        }
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
}
