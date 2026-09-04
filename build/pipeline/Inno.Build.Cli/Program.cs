using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Inno.Assets;
using Inno.Assets.Pipeline;
using Inno.Build.Platform.MacOS;
using Inno.Build.Platform.Windows;
using Inno.Core.Identity;
using Inno.Core.Settings;
using Inno.Plugins.Authoring;
using Inno.Runtime;
using Inno.Scene;
using Inno.Scripting.Compiler;

namespace Inno.Build.Cli;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        try
        {
            BuildCommand command = BuildCommand.Parse(args);
            using var cancellation = new CancellationTokenSource();
            Console.CancelKeyPress += (_, eventArgs) =>
            {
                eventArgs.Cancel = true;
                cancellation.Cancel();
            };
            using BuildWorkspace workspace = BuildWorkspace.Open(
                command.projectDirectory,
                command.supportPackRoot);
            BuildResult result = command.kind switch
            {
                BuildCommandKind.Game => await workspace.pipeline.BuildGameAsync(
                    command.CreateGameRequest(workspace.LoadGameProfile(command.profilePath)),
                    new ConsoleBuildProgress(),
                    cancellation.Token),
                BuildCommandKind.Plugin => await workspace.pipeline.BuildPluginAsync(
                    command.CreatePluginRequest(workspace.projectId),
                    new ConsoleBuildProgress(),
                    cancellation.Token),
                _ => throw new InvalidOperationException("Unknown build command kind.")
            };
            foreach (BuildDiagnostic diagnostic in result.diagnostics)
                Console.Error.WriteLine($"{diagnostic.severity} {diagnostic.code}: {diagnostic.message}");
            if (!result.succeeded)
                return 1;
            Console.WriteLine(result.outputPath);
            return 0;
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("Build canceled; no partial output was committed.");
            return 2;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
    }

    private sealed class ConsoleBuildProgress : IProgress<BuildProgress>
    {
        /// <summary>
        /// Publishes one progress update to the receiving workflow.
        /// </summary>
        /// <param name="value">
        /// The concrete value read or transformed by this operation.
        /// </param>
        public void Report(BuildProgress value)
            => Console.Error.WriteLine($"[{value.fraction:P0}] {value.message}");
    }
}

internal sealed class BuildWorkspace : IDisposable
{
    private readonly EngineHost m_engine;
    private readonly ProjectSettingsStore m_settings;
    private readonly AssetPipeline m_assets;
    private readonly PluginEnvironment m_plugins;
    private readonly string m_projectDirectory;

    private BuildWorkspace(
        EngineHost engine,
        ProjectSettingsStore settings,
        AssetPipeline assets,
        PluginEnvironment plugins,
        string projectDirectory,
        BuildPipeline pipeline)
    {
        m_engine = engine;
        m_settings = settings;
        m_assets = assets;
        m_plugins = plugins;
        m_projectDirectory = projectDirectory;
        this.pipeline = pipeline;
    }

    internal BuildPipeline pipeline { get; }
    internal ProjectId projectId => m_settings.projectId;


    internal static BuildWorkspace Open(string projectDirectory, string supportPackRoot)
    {
        string projectRoot = Path.GetFullPath(projectDirectory);
        string assetsRoot = Path.Combine(projectRoot, "Assets");
        string pluginsRoot = Path.Combine(projectRoot, "Plugins");
        string libraryRoot = Path.Combine(projectRoot, "Library");
        if (!Directory.Exists(assetsRoot))
            throw new DirectoryNotFoundException($"Project Assets directory '{assetsRoot}' does not exist.");
        Directory.CreateDirectory(pluginsRoot);
        Directory.CreateDirectory(libraryRoot);

        EngineHost? engine = null;
        ProjectSettingsStore? settings = null;
        AssetPipeline? assets = null;
        PluginEnvironment? plugins = null;
        try
        {
            engine = new EngineHostBuilder()
                .UseMetadataCache(Path.Combine(libraryRoot, "Build", "Metadata"))
                .Build();
            settings = new ProjectSettingsStore(
                Path.Combine(projectRoot, SettingsFileNames.project),
                engine.types,
                engine.serialization,
                ProjectId.FromName(Path.GetFileName(projectRoot)));
            var sources = new PluginSourceService(engine.serialization, pluginsRoot, libraryRoot);
            PluginScanResult scan = sources.Scan();
            AssetPipelineOptions options = AssetPipelineOptions.Create(assetsRoot, libraryRoot);
            AssetSourceMount projectMount = options.sourceMounts!.Single(static mount =>
                mount.id == AssetSourceId.project);
            assets = new AssetPipeline(
                engine.modules,
                engine.types,
                engine.serialization,
                new IdentityAllocator(),
                engine.diagnostics,
                engine.logs,
                options with
                {
                    enableFileSystemWatcher = false,
                    sourceMounts =
                    [
                        projectMount,
                        .. PluginSourceService.GetActivatableMounts(scan)
                    ]
                });
            plugins = new PluginEnvironment(
                assets,
                settings,
                engine.serialization,
                pluginsRoot,
                libraryRoot,
                scan);
            var compiler = new ScriptCompiler(
                new ScriptCompilerOptions
                {
                    projectRootDirectory = projectRoot
                },
                assets,
                plugins);
            var pipeline = new BuildPipeline(
                assets,
                plugins,
                settings,
                engine.serialization,
                compiler,
                supportPackRoot,
                [
                    new MacOSArm64GameBuildTarget(assets, engine.serialization),
                    new WindowsX64GameBuildTarget(assets, engine.serialization)
                ]);
            return new BuildWorkspace(engine, settings, assets, plugins, projectRoot, pipeline);
        }
        catch
        {
            plugins?.Dispose();
            assets?.Dispose();
            settings?.Dispose();
            engine?.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Releases the resources owned by this instance.
    /// </summary>
    public void Dispose()
    {
        m_plugins.Dispose();
        m_assets.Dispose();
        m_settings.Dispose();
        m_engine.Dispose();
    }

    internal BuildProfile LoadGameProfile(string? path)
    {
        if (!string.IsNullOrWhiteSpace(path))
        {
            string profilePath = Path.GetFullPath(path, m_projectDirectory);
            BuildProfile profile = new BuildProfileStore(profilePath, m_engine.serialization).Load();
            profile.applicationId = projectId.value;
            return profile;
        }

        BuildTargetId defaultTarget = OperatingSystem.IsWindows()
            ? BuildTargetId.windowsX64
            : BuildTargetId.macOSArm64;
        BuildSettings defaults = BuildSettings.CreateDefault(
            Path.GetFileName(Path.TrimEndingDirectorySeparator(m_projectDirectory)),
            FindDefaultStartupScene(),
            defaultTarget);
        var settings = new BuildSettingsStore(
            Path.Combine(m_projectDirectory, SettingsFileNames.build),
            m_engine.serialization,
            defaults);
        return settings.Load().CreateGameProfile(projectId);
    }

    private string FindDefaultStartupScene()
    {
        foreach (AssetFileEntry entry in m_assets.GetFileSystemEntries(includeDirectories: false)
                     .Where(static entry => entry.source == AssetSourceId.project)
                     .Where(static entry => !AssetSample.IsRuntimeExcluded(entry.assetPath, isDirectory: false))
                     .OrderBy(static entry => entry.assetPath.localPath, StringComparer.Ordinal))
        {
            if (m_assets.TryGetAssetType(entry.assetPath, out Type? type) && type == typeof(SceneAsset))
                return entry.assetPath.ToString();
        }
        return string.Empty;
    }
}

internal enum BuildCommandKind
{
    Game,
    Plugin
}

internal sealed class BuildCommand
{
    private readonly IReadOnlyDictionary<string, string> m_values;

    private BuildCommand(BuildCommandKind kind, IReadOnlyDictionary<string, string> values)
    {
        this.kind = kind;
        m_values = values;
        projectDirectory = Require(values, "project");
        supportPackRoot = kind == BuildCommandKind.Game
            ? Require(values, "support-packs")
            : values.GetValueOrDefault("support-packs", Path.Combine(AppContext.BaseDirectory, "SupportPacks"));
    }

    internal BuildCommandKind kind { get; }

    internal string projectDirectory { get; }

    internal string supportPackRoot { get; }

    internal string? profilePath => m_values.GetValueOrDefault("profile");

    internal static BuildCommand Parse(IReadOnlyList<string> args)
    {
        if (args.Count == 0 || args[0] is "help" or "--help" or "-h")
            throw new ArgumentException(Usage());
        BuildCommandKind kind = args[0] switch
        {
            "game" => BuildCommandKind.Game,
            "plugin" => BuildCommandKind.Plugin,
            _ => throw new ArgumentException($"Unknown command '{args[0]}'.{Environment.NewLine}{Usage()}")
        };
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int index = 1; index < args.Count; index++)
        {
            string argument = args[index];
            if (!argument.StartsWith("--", StringComparison.Ordinal))
                throw new ArgumentException($"Unexpected argument '{argument}'.");
            string key = argument[2..];
            if (key == "include-dependencies")
            {
                values.Add(key, bool.TrueString);
                continue;
            }
            if (++index >= args.Count)
                throw new ArgumentException($"Argument '--{key}' requires a value.");
            values.Add(key, args[index]);
        }
        return new BuildCommand(kind, values);
    }

    internal GameBuildRequest CreateGameRequest(BuildProfile profile)
        => new()
        {
            profile = profile ?? throw new ArgumentNullException(nameof(profile)),
            outputDirectory = Require(m_values, "output")
        };

    internal PluginBuildRequest CreatePluginRequest(ProjectId projectId)
        => new()
        {
            pluginId = projectId.value,
            displayName = Require(m_values, "display-name"),
            outputPath = Require(m_values, "output"),
            includeDependencies = m_values.ContainsKey("include-dependencies")
        };

    private static string Require(IReadOnlyDictionary<string, string> values, string key)
        => values.TryGetValue(key, out string? value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new ArgumentException($"Required argument '--{key}' is missing.");

    private static string Usage()
        => "Usage:\n"
           + "  Inno.Build.Cli game --project <dir> --support-packs <dir> --output <dir> [--profile <BuildProfile.inno>]\n"
           + "  Inno.Build.Cli plugin --project <dir> --output <package.iplugin> --display-name <name> [--include-dependencies]";
}
