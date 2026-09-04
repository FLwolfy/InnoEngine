using System;
using System.IO;
using System.Runtime.ExceptionServices;

using Inno.Assets;
using Inno.Assets.Pipeline;
using Inno.Core.Logging;
using Inno.Extensibility.Types;
using Inno.Editor.Core;
using Inno.Editor.Inspection;
using Inno.Editor.Interactions;
using Inno.Editor.Settings;
using Inno.Plugins.Authoring;
using static Inno.Editor.Panel.FileBrowser.FileBrowserUtility;

namespace Inno.Editor.Panel.FileBrowser;

/// <summary>
/// Owns shared Asset Browser state and asset-type extension dispatch.
/// </summary>
[EditorModule("asset-browser", order: 100)]
public sealed class AssetEditorModule : EditorModule, IInspectionIconProvider<AssetFileEntry>
{
    private readonly AssetPipeline m_pipeline;
    private readonly AssetEditorRegistry m_editors;
    private readonly AssetIconRegistry m_icons;
    private readonly EditorSettings m_settings;
    private readonly EditorInteractions m_interactions;
    private readonly Logger m_log;
    private readonly PluginEnvironment m_plugins;
    private EditorContext? m_context;

    /// <summary>
    /// Creates the Asset Browser feature module.
    /// </summary>
    /// <param name="interactions">
    /// The active editor interaction entry point.
    /// </param>
    /// <param name="settings">
    /// The project Settings service that owns semantic icon values.
    /// </param>
    /// <param name="pipeline">
    /// The isolated authoring asset pipeline used by browser operations.
    /// </param>
    /// <param name="plugins">
    /// The active Plugin catalog that authoritatively identifies Plugin-owned source mounts.
    /// </param>
    /// <param name="types">
    /// The active type catalog used to build extension registry snapshots.
    /// </param>
    /// <param name="logs">
    /// The application log router used for Asset Browser operation diagnostics.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when any required service is <see langword="null"/>.
    /// </exception>
    public AssetEditorModule(
        EditorInteractions interactions,
        EditorSettings settings,
        AssetPipeline pipeline,
        PluginEnvironment plugins,
        TypeCatalog types,
        LogRouter logs)
    {
        m_interactions = interactions ?? throw new ArgumentNullException(nameof(interactions));
        m_settings = settings ?? throw new ArgumentNullException(nameof(settings));
        m_pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
        m_plugins = plugins ?? throw new ArgumentNullException(nameof(plugins));
        ArgumentNullException.ThrowIfNull(types);
        ArgumentNullException.ThrowIfNull(logs);
        m_log = logs.CreateLogger<AssetEditorModule>();
        m_editors = new AssetEditorRegistry(types);
        m_icons = new AssetIconRegistry(m_settings, types);
        browser = new AssetBrowserState(interactions, pipeline);
    }

    /// <summary>
    /// Gets shared Asset Browser navigation and selection state.
    /// </summary>
    public AssetBrowserState browser { get; }

    /// <summary>
    /// Captures an immutable snapshot of the current observable state.
    /// </summary>
    /// <param name="state">
    /// The lifecycle or domain state applied by this operation.
    /// </param>
    protected override void Capture(EditorState state)
    {
        state.Set("root", browser.root.ToString());
        state.Set("assetsDirectory", browser.GetDirectory(AssetBrowserRoot.Assets));
        state.Set("pluginsDirectory", browser.GetDirectory(AssetBrowserRoot.Plugins));
    }

    /// <summary>
    /// Restores the supplied snapshot while preserving current invariants.
    /// </summary>
    /// <param name="state">
    /// The lifecycle or domain state applied by this operation.
    /// </param>
    protected override void Restore(EditorState state)
    {
        AssetBrowserRoot restoredRoot = Enum.TryParse(
            state.Get("root", string.Empty),
            out AssetBrowserRoot parsedRoot)
            ? parsedRoot
            : AssetBrowserRoot.Assets;
        string assetsDirectory = RestoreDirectory(
            AssetBrowserRoot.Assets,
            state.Get("assetsDirectory", string.Empty));
        string pluginsDirectory = RestoreDirectory(
            AssetBrowserRoot.Plugins,
            state.Get("pluginsDirectory", string.Empty));
        browser.Restore(restoredRoot, assetsDirectory, pluginsDirectory);
    }

    private string RestoreDirectory(AssetBrowserRoot root, string path)
    {
        string directory = NormalizePath(path);
        if (string.IsNullOrEmpty(directory))
            return string.Empty;
        AssetPath isolated = AssetPath.Parse(directory);
        if ((root == AssetBrowserRoot.Assets) != (isolated.source == AssetSourceId.project))
            return string.Empty;
        while (!IsAvailableDirectory(directory))
        {
            string parent = GetParentDirectory(directory);
            if (string.Equals(parent, directory, StringComparison.Ordinal))
                return string.Empty;
            directory = parent;
        }
        return directory;
    }

    private bool IsAvailableDirectory(string path)
        => m_pipeline.TryGetFileSystemEntry(AssetPath.Parse(path), out AssetFileEntry entry)
           && entry.isDirectory;

    internal EditorInteractions interactions => m_interactions;

    internal AssetPipeline pipeline => m_pipeline;

    internal bool IsPluginSource(AssetSourceId source)
        => m_plugins.TryGet(source, out _);

    internal bool IsPluginRoot(AssetFileEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return string.IsNullOrEmpty(entry.assetPath.localPath) && IsPluginSource(entry.source);
    }

    internal string folderIcon => m_settings
        .Get("Editor/Appearance/Icons/Folder")
        .GetAsString("value", Inno.Platform.Sdl3.ImGui.ImGuiIcon.Folder)!;

    internal bool TryCreateContext(
        EditorContext editor,
        string relativePath,
        out AssetEditorContext? context)
    {
        if (!m_pipeline.TryGetFileSystemEntry(AssetPath.Parse(relativePath), out AssetFileEntry entry))
        {
            context = null;
            return false;
        }
        _ = m_pipeline.TryGetInfo(AssetPath.Parse(relativePath), out AssetInfo? info);
        _ = m_pipeline.TryGetAssetType(AssetPath.Parse(relativePath), out Type? assetType);
        context = new AssetEditorContext(
            editor,
            m_interactions,
            entry.assetPath.ToString(),
            entry.name,
            entry.isDirectory,
            info,
            assetType,
            () => CreateDefaultDragData(entry, info));
        return true;
    }

    internal bool Open(AssetEditorContext context)
    {
        AssetEditor editor = m_editors.Resolve(context.assetType);
        if (!editor.CanOpen(context))
            return false;
        try
        {
            editor.Open(context);
            return true;
        }
        catch (Exception exception)
        {
            m_log.Write(
                LogLevel.Error,
                "Failed to open asset '{0}': {1}",
                [context.relativePath, exception]);
            return false;
        }
    }

    internal EditorValidationResult ValidateRename(
        AssetEditorContext context,
        string newName)
    {
        AssetEditor editor = m_editors.Resolve(context.assetType);
        return ToEditorValidation(ValidateRename(
            editor,
            context,
            Normalize(context.relativePath),
            newName,
            validateEditor: true,
            out _));
    }

    internal void Rename(AssetEditorContext context, string newName)
    {
        AssetEditor editor = m_editors.Resolve(context.assetType);
        AssetOperationValidation validation = ValidateRename(
            editor,
            context,
            Normalize(context.relativePath),
            newName,
            validateEditor: false,
            out string targetPath);
        if (!validation.isValid)
            throw new InvalidOperationException(validation.message);
        string sourcePath = Normalize(context.relativePath);
        EditorHistoryResult result = MoveAsset(editor, context, sourcePath, targetPath);
        if (!result.succeeded)
            throw new InvalidOperationException(result.message);
        var data = new AssetHistoryData(
            AssetHistoryOperationKind.Move,
            sourcePath,
            targetPath,
            isDirectory: context.isDirectory,
            archive: []);
        try
        {
            m_interactions.history.RecordApplied(
                "Rename Asset",
                new EditorHistoryChange(
                    AssetHistoryKinds.SourceOperation,
                    EditorHistoryPayload.FromBytes(data.Encode())));
        }
        catch (Exception exception)
        {
            RollbackAndRethrow(
                exception,
                () => RequireMove(editor, context, targetPath, sourcePath));
        }
    }

    /// <summary>
    /// Determines whether an asset source can move into a target directory without conflicts.
    /// </summary>
    /// <param name="sourcePath">
    /// The current source-relative asset path.
    /// </param>
    /// <param name="targetDirectory">
    /// The destination source-relative directory, or an empty string for the root.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the requested move is valid.
    /// </returns>
    internal bool CanMoveToDirectory(string sourcePath, string targetDirectory)
        => TryPrepareMove(
            sourcePath,
            targetDirectory,
            validateEditor: true,
            out _,
            out _,
            out _);

    /// <summary>
    /// Moves an asset into a directory and records the committed path transaction in Editor history.
    /// </summary>
    /// <param name="sourcePath">
    /// The current source-relative asset path.
    /// </param>
    /// <param name="targetDirectory">
    /// The destination source-relative directory, or an empty string for the root.
    /// </param>
    /// <param name="history">
    /// The active history that receives the committed move.
    /// </param>
    /// <returns>
    /// The source entry at its committed destination path.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="history"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when validation or the Asset transaction fails.
    /// </exception>
    internal AssetFileEntry MoveToDirectoryWithHistory(
        string sourcePath,
        string targetDirectory,
        IEditorHistory history)
    {
        ArgumentNullException.ThrowIfNull(history);
        if (!TryPrepareMove(
                sourcePath,
                targetDirectory,
                validateEditor: true,
                out AssetEditorContext? context,
                out AssetEditor? editor,
                out string targetPath) ||
            context is null ||
            editor is null)
        {
            throw new InvalidOperationException(
                $"Asset '{sourcePath}' cannot be moved to '{targetDirectory}'.");
        }

        string normalizedSource = Normalize(sourcePath);
        EditorHistoryResult result = MoveAsset(editor, context, normalizedSource, targetPath);
        if (!result.succeeded)
            throw new InvalidOperationException(result.message);

        var data = new AssetHistoryData(
            AssetHistoryOperationKind.Move,
            normalizedSource,
            targetPath,
            context.isDirectory,
            archive: []);
        try
        {
            history.RecordApplied(
                "Move Asset",
                new EditorHistoryChange(
                    AssetHistoryKinds.SourceOperation,
                    EditorHistoryPayload.FromBytes(data.Encode())));
        }
        catch (Exception exception)
        {
            RollbackAndRethrow(
                exception,
                () => RequireMove(editor, context, targetPath, normalizedSource));
        }

        return m_pipeline.TryGetFileSystemEntry(AssetPath.Parse(targetPath), out AssetFileEntry moved)
            ? moved
            : throw new InvalidOperationException(
                $"Moved asset '{targetPath}' is unavailable after the transaction.");
    }

    internal bool Delete(AssetEditorContext context)
    {
        AssetEditor editor = m_editors.Resolve(context.assetType);
        AssetOperationValidation validation = editor.ValidateDelete(context);
        if (!validation.isValid)
        {
            m_log.Write(
                LogLevel.Warn,
                "Asset delete was rejected for '{0}': {1}",
                [context.relativePath, validation.message]);
            return false;
        }
        string path = context.relativePath;
        m_pipeline.Delete(AssetPath.Parse(path));
        try
        {
            editor.OnDeleted(context);
        }
        catch (Exception exception)
        {
            m_log.Write(
                LogLevel.Error,
                "Asset editor delete hook failed for '{0}': {1}",
                [path, exception]);
        }
        return true;
    }

    internal bool DeleteWithHistory(EditorActionContext context, AssetEditorContext asset)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(asset);
        byte[] archive = AssetSourceArchive.Capture(m_pipeline, asset.relativePath, out bool isDirectory);
        if (!Delete(asset))
            return false;
        var data = new AssetHistoryData(
            AssetHistoryOperationKind.Delete,
            Normalize(asset.relativePath),
            string.Empty,
            isDirectory,
            archive);
        try
        {
            context.history.RecordApplied(
                "Delete Asset",
                new EditorHistoryChange(
                    AssetHistoryKinds.SourceOperation,
                    EditorHistoryPayload.FromBytes(data.Encode())));
        }
        catch (Exception exception)
        {
            RollbackAndRethrow(
                exception,
                () => AssetSourceArchive.Restore(
                    m_pipeline,
                    Normalize(asset.relativePath),
                    isDirectory,
                    archive));
        }
        return true;
    }

    internal void MoveFromHistory(string sourcePath, string targetPath)
    {
        EditorContext editorContext = m_context
            ?? throw new InvalidOperationException("Asset editor module is not attached.");
        if (!TryCreateContext(editorContext, sourcePath, out AssetEditorContext? context) || context is null)
            throw new InvalidOperationException($"Asset '{sourcePath}' is no longer available.");
        AssetEditor editor = m_editors.Resolve(context.assetType);
        EditorHistoryResult result = MoveAsset(editor, context, sourcePath, targetPath);
        if (!result.succeeded)
            throw new InvalidOperationException(result.message);
    }

    internal void DeleteFromHistory(string relativePath)
    {
        EditorContext editorContext = m_context
            ?? throw new InvalidOperationException("Asset editor module is not attached.");
        if (!TryCreateContext(editorContext, relativePath, out AssetEditorContext? context) || context is null)
            throw new InvalidOperationException($"Asset '{relativePath}' is no longer available.");
        if (!Delete(context))
            throw new InvalidOperationException($"Asset '{relativePath}' could not be deleted.");
    }

    internal void SelectPath(string relativePath)
    {
        object? target = m_pipeline.TryGetFileSystemEntry(AssetPath.Parse(relativePath), out AssetFileEntry entry)
            ? entry
            : null;
        _ = m_interactions.For(FileBrowserInteractionIds.C_AREA, target).Select();
    }

    internal bool TryCreateDragData(AssetEditorContext context, out EditorDragData? data)
    {
        AssetEditor editor = m_editors.Resolve(context.assetType);
        if (!editor.CanStartDrag(context))
        {
            data = null;
            return false;
        }
        data = editor.CreateDragData(context);
        return data is not null;
    }

    /// <summary>
    /// Resolves the presentation icon registered for an asset type or source extension.
    /// </summary>
    /// <param name="entry">
    /// The source entry whose presentation icon should be resolved.
    /// </param>
    /// <returns>
    /// The most specific registered icon, or the built-in directory or file icon when no
    /// registration matches the entry.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="entry"/> is <see langword="null"/>.
    /// </exception>
    public string GetIcon(AssetFileEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (entry.isDirectory)
            return folderIcon;
        _ = m_pipeline.TryGetAssetType(entry.assetPath, out Type? assetType);
        if (m_icons.TryResolve(assetType, entry.assetPath.ToString(), out string icon))
        {
            return icon;
        }
        return m_settings
            .Get("Editor/Appearance/Icons/File")
            .GetAsString("value", Inno.Platform.Sdl3.ImGui.ImGuiIcon.File)!;
    }

    /// <summary>
    /// Initializes this feature when its owning runtime becomes active.
    /// </summary>
    /// <param name="context">
    /// The context that supplies state and services for this operation.
    /// </param>
    protected override void OnStart(EditorContext context)
    {
        m_context = context ?? throw new ArgumentNullException(nameof(context));
    }

    /// <summary>
    /// Stops this feature before its owning runtime releases the active generation.
    /// </summary>
    /// <param name="context">
    /// The context that supplies state and services for this operation.
    /// </param>
    protected override void OnStop(EditorContext context)
    {
        m_context = null;
    }

    /// <summary>
    /// Releases resources retained by this feature after it has stopped.
    /// </summary>
    protected override void OnDispose()
    {
        m_editors.Dispose();
        m_icons.Dispose();
    }

    private AssetOperationValidation ValidateRename(
        AssetEditor editor,
        AssetEditorContext context,
        string sourcePath,
        string newName,
        bool validateEditor,
        out string targetPath)
    {
        newName = newName.Trim();
        targetPath = string.Empty;
        if (string.IsNullOrEmpty(newName))
            return AssetOperationValidation.Invalid("An asset name is required.");
        if (newName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            newName.Contains('/') || newName.Contains('\\'))
        {
            return AssetOperationValidation.Invalid(
                "Asset names cannot contain path separators or invalid file-name characters.");
        }

        string renamedEntry = ComposeRenamedEntryName(sourcePath, newName, context.isDirectory);
        string parent = Normalize(Path.GetDirectoryName(sourcePath));
        targetPath = string.IsNullOrEmpty(parent) ? renamedEntry : $"{parent}/{renamedEntry}";
        if (!string.Equals(sourcePath, targetPath, StringComparison.OrdinalIgnoreCase) &&
            m_pipeline.TryGetFileSystemEntry(AssetPath.Parse(targetPath), out _))
        {
            return AssetOperationValidation.Invalid($"Asset '{targetPath}' already exists.");
        }
        return validateEditor
            ? editor.ValidateRename(context, targetPath)
            : AssetOperationValidation.valid;
    }

    private bool TryPrepareMove(
        string sourcePath,
        string targetDirectory,
        bool validateEditor,
        out AssetEditorContext? context,
        out AssetEditor? editor,
        out string targetPath)
    {
        context = null;
        editor = null;
        targetPath = string.Empty;
        string source = Normalize(sourcePath);
        string directory = Normalize(targetDirectory);
        AssetPath isolatedSource = AssetPath.Parse(source);
        AssetPath isolatedDirectory = AssetPath.Parse(directory);
        if (isolatedSource.source != AssetSourceId.project ||
            isolatedDirectory.source != AssetSourceId.project)
        {
            return false;
        }
        EditorContext? editorContext = m_context;
        if (editorContext is null ||
            string.IsNullOrEmpty(source) ||
            !TryCreateContext(editorContext, source, out context) ||
            context is null)
        {
            return false;
        }
        if (!string.IsNullOrEmpty(directory) &&
            (!m_pipeline.TryGetFileSystemEntry(AssetPath.Parse(directory), out AssetFileEntry destination) ||
             !destination.isDirectory))
        {
            return false;
        }

        string parent = Normalize(Path.GetDirectoryName(source));
        if (string.Equals(parent, directory, StringComparison.OrdinalIgnoreCase))
            return false;
        if (context.isDirectory &&
            (string.Equals(source, directory, StringComparison.OrdinalIgnoreCase) ||
             directory.StartsWith(source + "/", StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        string name = Path.GetFileName(source);
        targetPath = string.IsNullOrEmpty(directory) ? name : $"{directory}/{name}";
        if (m_pipeline.TryGetFileSystemEntry(AssetPath.Parse(targetPath), out _))
            return false;

        editor = m_editors.Resolve(context.assetType);
        return !validateEditor || editor.ValidateRename(context, targetPath).isValid;
    }

    private EditorHistoryResult MoveAsset(
        AssetEditor editor,
        AssetEditorContext context,
        string sourcePath,
        string targetPath)
    {
        m_pipeline.Move(AssetPath.Parse(sourcePath), AssetPath.Parse(targetPath));
        try
        {
            editor.OnRenamed(context, sourcePath, targetPath);
        }
        catch (Exception exception)
        {
            m_log.Write(
                LogLevel.Error,
                "Asset editor rename hook failed for '{0}': {1}",
                [targetPath, exception]);
        }
        return EditorHistoryResult.Success();
    }

    private void RequireMove(
        AssetEditor editor,
        AssetEditorContext context,
        string sourcePath,
        string targetPath)
    {
        EditorHistoryResult result = MoveAsset(editor, context, sourcePath, targetPath);
        if (!result.succeeded)
            throw new InvalidOperationException(result.message);
    }

    private static void RollbackAndRethrow(Exception failure, Action rollback)
    {
        try
        {
            rollback();
        }
        catch (Exception rollbackException)
        {
            throw new AggregateException(
                "An Asset mutation could not be recorded and its compensation also failed.",
                failure,
                rollbackException);
        }
        ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static EditorValidationResult ToEditorValidation(AssetOperationValidation validation)
        => new(validation.isValid, validation.message);

    private static string Normalize(string? path)
        => string.IsNullOrWhiteSpace(path)
            ? string.Empty
            : path.Replace('\\', '/').Trim('/');

    private EditorDragData CreateDefaultDragData(AssetFileEntry entry, AssetInfo? info)
    {
        if (entry.isDirectory)
        {
            return new EditorDragData(
                entry,
                entry.name,
                () => m_pipeline.TryGetFileSystemEntry(entry.assetPath, out AssetFileEntry current) &&
                      current.isDirectory);
        }

        return new EditorDragData(
            info ?? (object)entry,
            entry.name,
            () => info is { persistentId: var id } &&
                  id != Guid.Empty &&
                  m_pipeline.TryGetInfo(id, out AssetInfo? current) &&
                  current?.status is not AssetImportStatus.Missing and not AssetImportStatus.Conflict);
    }
}
