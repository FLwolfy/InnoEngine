using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Inno.Assets;
using Inno.Assets.Pipeline;
using Inno.Build;
using Inno.Core.Logging;
using Inno.Editor.Core;
using Inno.Editor.Scene;
using Inno.Editor.Settings;
using Inno.Scene;

namespace Inno.Editor.Exporting;

[EditorModule("exporting", order: 300)]
internal sealed class ExportWindowModule : EditorModule
{
    private readonly EditorContext m_editor;
    private readonly AssetPipeline m_assets;
    private readonly EditorSettings m_settings;
    private readonly IEditorSceneWorkspace m_scenes;
    private readonly BuildPipeline m_buildPipeline;
    private readonly BuildProfileStore m_buildProfiles;
    private readonly Logger m_log;
    private readonly ConcurrentQueue<BuildProgress> m_progress = new();
    private CancellationTokenSource? m_cancellation;
    private Task<BuildResult>? m_gameExport;
    private Task<BuildResult>? m_pluginExport;

    internal ExportWindowModule(
        EditorContext editor,
        AssetPipeline assets,
        EditorSettings settings,
        IEditorSceneWorkspace scenes,
        BuildPipeline buildPipeline,
        BuildProfileStore buildProfiles,
        LogRouter logs)
    {
        m_editor = editor ?? throw new ArgumentNullException(nameof(editor));
        m_assets = assets ?? throw new ArgumentNullException(nameof(assets));
        m_settings = settings ?? throw new ArgumentNullException(nameof(settings));
        m_scenes = scenes ?? throw new ArgumentNullException(nameof(scenes));
        m_buildPipeline = buildPipeline ?? throw new ArgumentNullException(nameof(buildPipeline));
        m_buildProfiles = buildProfiles ?? throw new ArgumentNullException(nameof(buildProfiles));
        ArgumentNullException.ThrowIfNull(logs);
        m_log = logs.CreateLogger<ExportWindowModule>();
    }

    internal bool isPluginVisible { get; private set; }

    internal bool isGameVisible { get; private set; }

    internal bool isPluginBusy => m_pluginExport is not null;

    internal bool isGameBusy => m_gameExport is not null;

    internal string pluginId { get; set; } = string.Empty;

    internal string pluginDisplayName { get; set; } = string.Empty;

    internal string pluginOutputPath { get; set; } = string.Empty;

    internal string gameApplicationId { get; set; } = string.Empty;

    internal string gameProductName { get; set; } = string.Empty;

    internal string gameStartupScene { get; set; } = string.Empty;

    internal string gameOutputDirectory { get; set; } = string.Empty;

    internal int gameWindowWidth { get; set; } = 1280;

    internal int gameWindowHeight { get; set; } = 720;

    internal BuildTargetId gameTarget { get; set; } = BuildTargetId.macOSArm64;

    internal string status { get; private set; } = string.Empty;

    internal string error { get; private set; } = string.Empty;

    internal bool includePluginDependencies => EmbedPluginDependenciesSetting.Read(m_settings);

    internal void OpenPlugin()
    {
        CloseGame();
        string projectName = GetProjectName();
        pluginId = ToPortableId(projectName);
        pluginDisplayName = projectName;
        pluginOutputPath = Path.Combine(m_editor.projectDirectory, "Builds", pluginId + ".iplugin");
        status = string.Empty;
        error = string.Empty;
        isPluginVisible = true;
    }

    internal void OpenGame()
    {
        ClosePlugin();
        string projectName = GetProjectName();
        gameApplicationId = ToPortableId(projectName);
        gameProductName = projectName;
        gameStartupScene = FindDefaultStartupScene();
        gameOutputDirectory = Path.Combine(m_editor.projectDirectory, "Builds");
        gameWindowWidth = 1280;
        gameWindowHeight = 720;
        gameTarget = OperatingSystem.IsWindows()
            ? BuildTargetId.windowsX64
            : BuildTargetId.macOSArm64;
        status = string.Empty;
        error = string.Empty;
        if (m_buildProfiles.exists)
        {
            try
            {
                BuildProfile profile = m_buildProfiles.Load();
                gameApplicationId = profile.applicationId;
                gameProductName = profile.productName;
                gameStartupScene = profile.startupScene;
                gameTarget = profile.target;
                gameWindowWidth = profile.windowWidth;
                gameWindowHeight = profile.windowHeight;
            }
            catch (Exception exception)
            {
                error = exception.Message;
            }
        }
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
                outputPath = pluginOutputPath,
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
        try
        {
            m_buildProfiles.Save(profile);
        }
        catch (Exception exception)
        {
            status = string.Empty;
            error = exception.Message;
            return;
        }
        StartCancellation();
        status = "Capturing the authoring generation and compiling runtime scripts...";
        error = string.Empty;
        m_gameExport = m_buildPipeline.BuildGameAsync(
            new GameBuildRequest
            {
                profile = profile,
                outputDirectory = gameOutputDirectory
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

    private string FindDefaultStartupScene()
    {
        if (m_scenes.activeScene is GameScene activeScene
            && m_scenes.TryGetSourcePath(activeScene, out string activePath))
        {
            return activePath;
        }

        foreach (var entry in m_assets.GetFileSystemEntries(includeDirectories: false)
                     .Where(static entry => entry.source == AssetSourceId.project)
                     .OrderBy(static entry => entry.assetPath.localPath, StringComparer.Ordinal))
        {
            if (m_assets.TryGetAssetType(entry.assetPath, out Type? type) && type == typeof(SceneAsset))
                return entry.assetPath.ToString();
        }
        return string.Empty;
    }

    private string GetProjectName()
        => Path.GetFileName(Path.TrimEndingDirectorySeparator(m_editor.projectDirectory));

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

    private static string ToPortableId(string value)
    {
        string normalized = new(value
            .Trim()
            .ToLowerInvariant()
            .Select(static character => char.IsAsciiLetterOrDigit(character) ? character : '.')
            .ToArray());
        string[] segments = normalized.Split('.', StringSplitOptions.RemoveEmptyEntries);
        return segments.Length == 0 ? "inno.project" : string.Join('.', segments);
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
