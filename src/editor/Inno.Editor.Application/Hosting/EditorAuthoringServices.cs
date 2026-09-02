using System;
using System.IO;
using System.Linq;

using Inno.Assets;
using Inno.Assets.Pipeline;
using Inno.Plugins.Authoring;
using Inno.Core.Identity;
using Inno.Core.Logging;
using Inno.Core.Settings;
using Inno.Runtime;
using Inno.Scripting.Compiler;

namespace Inno.Editor.Application;

/// <summary>
/// Owns the authoring-only asset, Plugin, and project-settings services composed by the Editor process.
/// </summary>
internal sealed class EditorAuthoringServices : IDisposable
{
    private bool m_disposed;

    private EditorAuthoringServices(
        ProjectSettingsStore settings,
        AssetPipeline assets,
        PluginEnvironment plugins,
        ScriptCompiler compiler)
    {
        this.settings = settings;
        this.assets = assets;
        this.plugins = plugins;
        this.compiler = compiler;
    }

    internal AssetPipeline assets { get; }

    internal PluginEnvironment plugins { get; }

    internal ScriptCompiler compiler { get; }

    internal ProjectSettingsStore settings { get; }

    internal static EditorAuthoringServices Start(
        string projectDirectory,
        EngineHost engineHost)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectDirectory);
        ArgumentNullException.ThrowIfNull(engineHost);
        Logger log = engineHost.logs.CreateLogger<EditorAuthoringServices>();
        string projectRoot = Path.GetFullPath(projectDirectory);
        string assetRoot = Path.Combine(projectRoot, "Assets");
        string pluginRoot = Path.Combine(projectRoot, "Plugins");
        string libraryRoot = Path.Combine(projectRoot, "Library");
        Directory.CreateDirectory(assetRoot);
        Directory.CreateDirectory(pluginRoot);
        Directory.CreateDirectory(libraryRoot);

        var identities = new IdentityAllocator();
        ProjectSettingsStore? settings = null;
        AssetPipeline? assets = null;
        PluginEnvironment? plugins = null;
        try
        {
            settings = new ProjectSettingsStore(
                Path.Combine(projectRoot, "ProjectSettings.inno"),
                engineHost.types,
                engineHost.serialization);
            var pluginSources = new PluginSourceService(
                engineHost.serialization,
                pluginRoot,
                libraryRoot);
            PluginScanResult pluginScan = pluginSources.Scan();
            AssetPipelineOptions defaultOptions = AssetPipelineOptions.Create(assetRoot, libraryRoot);
            AssetSourceMount projectMount = defaultOptions.sourceMounts!.Single(
                static mount => mount.id == AssetSourceId.project);
            assets = new AssetPipeline(
                engineHost.modules,
                engineHost.types,
                engineHost.serialization,
                identities,
                engineHost.diagnostics,
                engineHost.logs,
                defaultOptions with
            {
                sourceMounts =
                [
                    projectMount,
                    .. PluginSourceService.GetActivatableMounts(pluginScan)
                ]
            });
            plugins = new PluginEnvironment(
                assets,
                settings,
                engineHost.serialization,
                pluginRoot,
                libraryRoot,
                pluginScan);
            foreach (PluginDiagnostic diagnostic in plugins.discovery.diagnostics)
            {
                log.Write(
                    LogLevel.Warn,
                    "Plugin source '{0}' was excluded from the active candidate: {1}",
                    [diagnostic.sourcePath, diagnostic.message]);
            }
            var compiler = new ScriptCompiler(
                new ScriptCompilerOptions
                {
                    projectRootDirectory = projectRoot
                },
                assets,
                plugins);
            return new EditorAuthoringServices(settings, assets, plugins, compiler);
        }
        catch
        {
            plugins?.Dispose();
            assets?.Dispose();
            settings?.Dispose();
            throw;
        }
    }

    internal void Update()
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);
        plugins.Update();
        assets.Update();
    }

    /// <summary>
    /// Releases the resources owned by this instance.
    /// </summary>
    public void Dispose()
    {
        if (m_disposed)
            return;
        m_disposed = true;
        plugins.Dispose();
        assets.Dispose();
        settings.Dispose();
    }
}
