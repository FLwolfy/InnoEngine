using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Inno.Build;
using Inno.Core.Logging;
using Inno.Core.Settings;
using Inno.Editor.Core;

namespace Inno.Editor.Exporting;

[EditorModule("exporting", order: 300)]
internal sealed class ExportWindowModule : EditorModule
{
    private readonly EditorContext m_editor;
    private readonly BuildPipeline m_buildPipeline;
    private readonly BuildSettingsStore m_buildSettings;
    private readonly ProjectSettingsStore m_projectSettings;
    private readonly Logger m_log;
    private readonly ConcurrentQueue<BuildProgress> m_progress = new();
    private CancellationTokenSource? m_cancellation;
    private Task<BuildResult>? m_gameExport;
    private Task<BuildResult>? m_pluginExport;

    internal ExportWindowModule(
        EditorContext editor,
        BuildPipeline buildPipeline,
        BuildSettingsStore buildSettings,
        ProjectSettingsStore projectSettings,
        LogRouter logs)
    {
        m_editor = editor ?? throw new ArgumentNullException(nameof(editor));
        m_buildPipeline = buildPipeline ?? throw new ArgumentNullException(nameof(buildPipeline));
        m_buildSettings = buildSettings ?? throw new ArgumentNullException(nameof(buildSettings));
        m_projectSettings = projectSettings ?? throw new ArgumentNullException(nameof(projectSettings));
        ArgumentNullException.ThrowIfNull(logs);
        m_log = logs.CreateLogger<ExportWindowModule>();
    }

    internal bool isPluginVisible { get; private set; }

    internal bool isGameVisible { get; private set; }

    internal bool isPluginBusy => m_pluginExport is not null;

    internal bool isGameBusy => m_gameExport is not null;

    internal string pluginId => m_projectSettings.projectId.value;

    internal string pluginDisplayName { get; set; } = string.Empty;

    internal string pluginOutputPath { get; set; } = string.Empty;

    internal string gameApplicationId => m_projectSettings.projectId.value;

    internal string gameProductName { get; set; } = string.Empty;

    internal string gameStartupScene { get; set; } = string.Empty;

    internal string gameOutputDirectory { get; set; } = string.Empty;

    internal int gameWindowWidth { get; set; } = 1280;

    internal int gameWindowHeight { get; set; } = 720;

    internal BuildTargetId gameTarget { get; set; } = BuildTargetId.macOSArm64;

    internal string status { get; private set; } = string.Empty;

    internal string error { get; private set; } = string.Empty;

    internal bool includePluginDependencies { get; set; }

    internal void OpenPlugin()
    {
        CloseGame();
        status = string.Empty;
        error = string.Empty;
        BuildSettings defaults = LoadBuildSettings();
        pluginDisplayName = defaults.pluginDisplayName;
        pluginOutputPath = defaults.pluginOutputPath;
        includePluginDependencies = defaults.includePluginDependencies;
        isPluginVisible = true;
    }

    internal void OpenGame()
    {
        ClosePlugin();
        status = string.Empty;
        error = string.Empty;
        BuildSettings defaults = LoadBuildSettings();
        gameProductName = defaults.gameProductName;
        gameStartupScene = defaults.gameStartupScene;
        gameOutputDirectory = defaults.gameOutputDirectory;
        gameWindowWidth = defaults.gameWindowWidth;
        gameWindowHeight = defaults.gameWindowHeight;
        gameTarget = defaults.gameTarget;
        isGameVisible = true;
    }

    internal void BeginPluginExport()
    {
        if (isPluginBusy)
            return;
        StartCancellation();
        status = "Capturing the current Plugin source and dependency generation...";
        error = string.Empty;
        m_pluginExport = m_buildPipeline.BuildPluginAsync(
            new PluginBuildRequest
            {
                pluginId = pluginId,
                displayName = pluginDisplayName,
                outputPath = ResolveOutputPath(pluginOutputPath),
                includeDependencies = includePluginDependencies
            },
            new BuildProgressSink(m_progress),
            m_cancellation!.Token).AsTask();
    }

    internal void CancelPluginExport()
    {
        if (!isPluginBusy)
            return;
        status = "Canceling Plugin export...";
        m_cancellation?.Cancel();
    }

    internal void BeginGameExport()
    {
        if (isGameBusy)
            return;
        var profile = new BuildProfile
        {
            applicationId = gameApplicationId,
            productName = gameProductName,
            startupScene = gameStartupScene,
            target = gameTarget,
            windowWidth = gameWindowWidth,
            windowHeight = gameWindowHeight
        };
        StartCancellation();
        status = "Capturing the authoring generation and compiling runtime scripts...";
        error = string.Empty;
        m_gameExport = m_buildPipeline.BuildGameAsync(
            new GameBuildRequest
            {
                profile = profile,
                outputDirectory = ResolveOutputPath(gameOutputDirectory)
            },
            new BuildProgressSink(m_progress),
            m_cancellation!.Token).AsTask();
    }

    internal void CancelGameExport()
    {
        status = "Canceling game build...";
        m_cancellation?.Cancel();
    }

    internal void ClosePlugin()
    {
        if (isPluginBusy)
            CancelPluginExport();
        isPluginVisible = false;
        if (!isPluginBusy)
        {
            status = string.Empty;
            error = string.Empty;
        }
    }

    internal void CloseGame()
    {
        if (isGameBusy)
            CancelGameExport();
        isGameVisible = false;
        if (!isGameBusy)
        {
            status = string.Empty;
            error = string.Empty;
        }
    }

    /// <summary>
    /// Advances compilation tickets, build progress, and completed export tasks.
    /// </summary>
    /// <param name="context">
    /// The active Editor context for the current frame.
    /// </param>
    protected override void OnUpdate(EditorContext context)
    {
        while (m_progress.TryDequeue(out BuildProgress progress))
            status = progress.message;
        CompletePluginExport();
        CompleteGameExport();
    }

    /// <summary>
    /// Cancels active export work before the Editor generation is stopped.
    /// </summary>
    /// <param name="context">
    /// The active Editor context being stopped.
    /// </param>
    protected override void OnStop(EditorContext context)
    {
        m_cancellation?.Cancel();
        isPluginVisible = false;
        isGameVisible = false;
    }

    /// <summary>
    /// Releases cancellation resources owned by this module.
    /// </summary>
    protected override void OnDispose()
    {
        m_cancellation?.Cancel();
        m_cancellation?.Dispose();
    }

    private void CompletePluginExport()
    {
        Task<BuildResult>? export = m_pluginExport;
        if (export is null || !export.IsCompleted)
            return;
        m_pluginExport = null;
        try
        {
            BuildResult result = export.GetAwaiter().GetResult();
            if (!result.succeeded)
            {
                ReportBuildFailure("Plugin export", result);
                return;
            }
            string outputPath = result.outputPath
                ?? throw new InvalidOperationException("A successful Plugin build has no output path.");
            string contentHash = result.contentHash
                ?? throw new InvalidOperationException("A successful Plugin build has no content identity.");
            status = $"Exported {result.assetCount} source files to {outputPath}";
            error = string.Empty;
            m_log.Write(
                LogLevel.Info,
                "Exported Plugin '{0}' to '{1}' ({2} assets, {3} embedded dependencies, {4}).",
                [pluginId, outputPath, result.assetCount, result.embeddedPluginCount, contentHash]);
        }
        catch (OperationCanceledException)
        {
            status = string.Empty;
            error = "Plugin export was canceled; no partial package was installed.";
            m_log.Write(LogLevel.Info, "Plugin export was canceled.");
        }
        catch (Exception exception)
        {
            status = string.Empty;
            error = exception.Message;
            m_log.Write(LogLevel.Error, "Plugin export failed: {0}", [exception]);
        }
        finally
        {
            ReleaseCancellation();
        }
    }

    private void CompleteGameExport()
    {
        Task<BuildResult>? export = m_gameExport;
        if (export is null || !export.IsCompleted)
            return;
        m_gameExport = null;
        try
        {
            BuildResult result = export.GetAwaiter().GetResult();
            if (!result.succeeded)
            {
                ReportBuildFailure("Game build", result);
                return;
            }
            string outputPath = result.outputPath
                ?? throw new InvalidOperationException("A successful game build has no output path.");
            status = $"Exported {result.assetCount} assets and {result.runtimeAssemblyCount} runtime assemblies to {outputPath}";
            error = string.Empty;
            m_log.Write(
                LogLevel.Info,
                "Exported game to '{0}' ({1}, {2} assets, {3} artifact bundles, {4} runtime assemblies).",
                [
                    outputPath,
                    result.target?.ToString() ?? "unknown",
                    result.assetCount,
                    result.artifactBundleCount,
                    result.runtimeAssemblyCount
                ]);
        }
        catch (OperationCanceledException)
        {
            status = string.Empty;
            error = "Game build was canceled; no partial output was installed.";
            m_log.Write(LogLevel.Info, "Game build was canceled.");
        }
        catch (Exception exception)
        {
            status = string.Empty;
            error = exception.Message;
            m_log.Write(LogLevel.Error, "Game build failed: {0}", [exception]);
        }
        finally
        {
            ReleaseCancellation();
        }
    }

    private BuildSettings LoadBuildSettings()
    {
        try
        {
            return m_buildSettings.Load();
        }
        catch (Exception exception) when (exception is IOException
            or InvalidDataException
            or InvalidOperationException
            or UnauthorizedAccessException)
        {
            error = exception.Message;
            return m_buildSettings.defaultSettings;
        }
    }

    private string ResolveOutputPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return path;
        return Path.IsPathFullyQualified(path)
            ? Path.GetFullPath(path)
            : Path.GetFullPath(path, m_editor.projectDirectory);
    }

    private void StartCancellation()
    {
        m_cancellation?.Dispose();
        m_cancellation = new CancellationTokenSource();
    }

    private void ReleaseCancellation()
    {
        m_cancellation?.Dispose();
        m_cancellation = null;
    }

    private void ReportBuildFailure(string operation, BuildResult result)
    {
        string message = string.Join(
            Environment.NewLine,
            result.diagnostics.Select(static diagnostic => $"[{diagnostic.code}] {diagnostic.message}"));
        status = string.Empty;
        error = message;
        m_log.Write(
            LogLevel.Error,
            "{0} failed:{1}{2}",
            [operation, Environment.NewLine, message]);
    }

    private sealed class BuildProgressSink(ConcurrentQueue<BuildProgress> progress) : IProgress<BuildProgress>
    {
        /// <summary>
        /// Publishes one progress update to the receiving workflow.
        /// </summary>
        /// <param name="value">
        /// The concrete value read or transformed by this operation.
        /// </param>
        public void Report(BuildProgress value)
            => progress.Enqueue(value);
    }
}
