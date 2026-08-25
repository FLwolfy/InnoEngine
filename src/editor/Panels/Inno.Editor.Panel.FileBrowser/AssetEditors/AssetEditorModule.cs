using System;
using System.IO;
using System.Runtime.ExceptionServices;

using Inno.Assets;
using Inno.Assets.Core;
using Inno.Assets.File;
using Inno.Core.Logging;
using Inno.Editor.Core;
using Inno.Editor.Inspection;
using Inno.Editor.Interactions;
using Inno.Editor.Settings;

namespace Inno.Editor.Panel.FileBrowser;

/// <summary>Owns shared Asset Browser state and asset-type extension dispatch.</summary>
[EditorModule("asset-browser", order: 100)]
public sealed class AssetEditorModule : EditorModule, IInspectionIconProvider<AssetFileEntry>
{
    private readonly AssetEditorRegistry m_editors = new();
    private readonly AssetIconRegistry m_icons;
    private readonly EditorSettings m_settings;
    private readonly EditorInteractions m_interactions;
    private EditorContext? m_context;

    /// <summary>Creates the Asset Browser feature module.</summary>
    /// <param name="interactions">The active editor interaction entry point.</param>
    /// <param name="settings">
    /// The project Settings service that owns semantic icon values.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="interactions"/> or <paramref name="settings"/> is
    /// <see langword="null"/>.
    /// </exception>
    public AssetEditorModule(
        EditorInteractions interactions,
        EditorSettings settings)
    {
        m_interactions = interactions ?? throw new ArgumentNullException(nameof(interactions));
        m_settings = settings ?? throw new ArgumentNullException(nameof(settings));
        m_icons = new AssetIconRegistry(m_settings);
        browser = new AssetBrowserState(interactions);
    }

    /// <summary>Gets shared Asset Browser navigation and selection state.</summary>
    public AssetBrowserState browser { get; }

    /// <inheritdoc />
    protected override void Capture(EditorState state)
    {
        state.Set("currentDirectory", browser.currentDirectory);
    }

    /// <inheritdoc />
    protected override void Restore(EditorState state)
    {
        string directory = state.Get("currentDirectory", string.Empty);
        while (!string.IsNullOrEmpty(directory) &&
               (!AssetManager.TryGetFileSystemEntry(directory, out AssetFileEntry entry) || !entry.isDirectory))
        {
            directory = Normalize(Path.GetDirectoryName(directory));
        }
        browser.SetCurrentDirectory(directory);
    }

    internal EditorInteractions interactions => m_interactions;

    internal string folderIcon => m_settings
        .Get("Global/Appearance/Icons/Folder")
        .GetAsString("value", Inno.Platform.ImGui.ImGuiIcon.Folder)!;

    internal bool TryCreateContext(
        EditorContext editor,
        string relativePath,
        out AssetEditorContext? context)
    {
        if (!AssetManager.TryGetFileSystemEntry(relativePath, out AssetFileEntry entry))
        {
            context = null;
            return false;
        }
        _ = AssetManager.TryGetInfo(relativePath, out AssetInfo? info);
        _ = AssetManager.TryGetAssetType(relativePath, out Type? assetType);
        context = new AssetEditorContext(
            editor,
            m_interactions,
            entry.relativePath,
            entry.name,
            entry.isDirectory,
            info,
            assetType);
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
            Log.Error("Failed to open asset '{0}': {1}", context.relativePath, exception);
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
    /// <param name="sourcePath">The current source-relative asset path.</param>
    /// <param name="targetDirectory">The destination source-relative directory, or an empty string for the root.</param>
    /// <returns><see langword="true"/> when the requested move is valid.</returns>
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
    /// <param name="sourcePath">The current source-relative asset path.</param>
    /// <param name="targetDirectory">The destination source-relative directory, or an empty string for the root.</param>
    /// <param name="history">The active history that receives the committed move.</param>
    /// <returns>The source entry at its committed destination path.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="history"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">Thrown when validation or the Asset transaction fails.</exception>
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

        return AssetManager.TryGetFileSystemEntry(targetPath, out AssetFileEntry moved)
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
            Log.Warn("Asset delete was rejected for '{0}': {1}", context.relativePath, validation.message);
            return false;
        }
        string path = context.relativePath;
        AssetManager.Delete(path);
        try
        {
            editor.OnDeleted(context);
        }
        catch (Exception exception)
        {
            Log.Error("Asset editor delete hook failed for '{0}': {1}", path, exception);
        }
        return true;
    }

    internal bool DeleteWithHistory(EditorActionContext context, AssetEditorContext asset)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(asset);
        byte[] archive = AssetSourceArchive.Capture(asset.relativePath, out bool isDirectory);
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
        object? target = AssetManager.TryGetFileSystemEntry(relativePath, out AssetFileEntry entry)
            ? entry
            : null;
        _ = m_interactions.For(FileBrowserInteractionIds.area, target).Select();
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
    /// <param name="entry">The source entry whose presentation icon should be resolved.</param>
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
        _ = AssetManager.TryGetAssetType(entry.relativePath, out Type? assetType);
        if (m_icons.TryResolve(assetType, entry.relativePath, out string icon))
        {
            return icon;
        }
        return m_settings
            .Get("Global/Appearance/Icons/File")
            .GetAsString("value", Inno.Platform.ImGui.ImGuiIcon.File)!;
    }

    /// <inheritdoc />
    protected override void OnStart(EditorContext context)
    {
        m_context = context ?? throw new ArgumentNullException(nameof(context));
    }

    /// <inheritdoc />
    protected override void OnStop(EditorContext context)
    {
        m_context = null;
    }

    /// <inheritdoc />
    protected override void OnDispose()
    {
        m_editors.Dispose();
        m_icons.Dispose();
    }

    private static AssetOperationValidation ValidateRename(
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

        string parent = Normalize(Path.GetDirectoryName(sourcePath));
        targetPath = string.IsNullOrEmpty(parent) ? newName : $"{parent}/{newName}";
        if (!string.Equals(sourcePath, targetPath, StringComparison.OrdinalIgnoreCase) &&
            AssetManager.TryGetFileSystemEntry(targetPath, out _))
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
        EditorContext? editorContext = m_context;
        if (editorContext is null ||
            string.IsNullOrEmpty(source) ||
            !TryCreateContext(editorContext, source, out context) ||
            context is null)
        {
            return false;
        }
        if (!string.IsNullOrEmpty(directory) &&
            (!AssetManager.TryGetFileSystemEntry(directory, out AssetFileEntry destination) ||
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
        if (AssetManager.TryGetFileSystemEntry(targetPath, out _))
            return false;

        editor = m_editors.Resolve(context.assetType);
        return !validateEditor || editor.ValidateRename(context, targetPath).isValid;
    }

    private static EditorHistoryResult MoveAsset(
        AssetEditor editor,
        AssetEditorContext context,
        string sourcePath,
        string targetPath)
    {
        AssetManager.Move(sourcePath, targetPath);
        try
        {
            editor.OnRenamed(context, sourcePath, targetPath);
        }
        catch (Exception exception)
        {
            Log.Error("Asset editor rename hook failed for '{0}': {1}", targetPath, exception);
        }
        return EditorHistoryResult.Success();
    }

    private static void RequireMove(
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
}
