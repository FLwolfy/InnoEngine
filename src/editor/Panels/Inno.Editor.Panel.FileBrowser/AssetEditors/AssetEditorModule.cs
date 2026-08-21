using System;
using System.IO;

using Inno.Assets;
using Inno.Assets.Core;
using Inno.Assets.File;
using Inno.Core.Logging;
using Inno.Editor.Core;
using Inno.Editor.Interactions;

namespace Inno.Editor.Panel.FileBrowser;

/// <summary>Owns shared Asset Browser state and asset-type extension dispatch.</summary>
[EditorModule(order: 100)]
public sealed class AssetEditorModule : EditorModule, IEditorWorkspaceState, IDisposable
{
    private const string C_WORKSPACE_STATE_ID = "asset-browser";

    private readonly AssetEditorRegistry m_editors = new();
    private readonly AssetIconRegistry m_icons = new();
    private readonly EditorInteractions m_interactions;
    private EditorContext? m_context;
    private bool m_disposed;

    /// <summary>Creates the Asset Browser feature module.</summary>
    /// <param name="interactions">The active editor interaction entry point.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="interactions"/> is <see langword="null"/>.</exception>
    public AssetEditorModule(EditorInteractions interactions)
    {
        m_interactions = interactions ?? throw new ArgumentNullException(nameof(interactions));
        browser = new AssetBrowserState(interactions);
    }

    /// <summary>Gets shared Asset Browser navigation and selection state.</summary>
    public AssetBrowserState browser { get; }

    /// <inheritdoc />
    public string workspaceStateId => C_WORKSPACE_STATE_ID;

    /// <inheritdoc />
    public void CaptureWorkspaceState(EditorWorkspaceStateWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.Set("currentDirectory", browser.currentDirectory);
    }

    /// <inheritdoc />
    public void RestoreWorkspaceState(EditorWorkspaceStateReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        string directory = reader.Get("currentDirectory", string.Empty);
        while (!string.IsNullOrEmpty(directory) &&
               (!AssetManager.TryGetFileSystemEntry(directory, out AssetFileEntry entry) || !entry.isDirectory))
        {
            directory = Normalize(Path.GetDirectoryName(directory));
        }
        browser.SetCurrentDirectory(directory);
    }

    internal EditorInteractions interactions => m_interactions;

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
        m_interactions.history.RecordApplied(
            "Rename Asset",
            new EditorHistoryChange(
                AssetHistoryKinds.SourceOperation,
                EditorHistoryPayload.FromBytes(data.Encode())));
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
        _ = context.interactions.For(FileBrowserAreas.Browser).Select();
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
        context.history.RecordApplied(
            "Delete Asset",
            new EditorHistoryChange(
                AssetHistoryKinds.SourceOperation,
                EditorHistoryPayload.FromBytes(data.Encode())));
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
        _ = m_interactions.For(FileBrowserAreas.Browser, target).Select();
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

    internal string ResolveIcon(AssetFileEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (entry.isDirectory)
            return Inno.Platform.ImGui.ImGuiIcon.Folder;
        _ = AssetManager.TryGetAssetType(entry.relativePath, out Type? assetType);
        if (m_icons.TryResolve(assetType, entry.relativePath, out string icon))
        {
            return icon;
        }
        return FileBrowserUtility.GetDefaultFileIcon();
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

    /// <summary>
    /// Releases the current asset-editor and asset-icon registry snapshots.
    /// </summary>
    public void Dispose()
    {
        if (m_disposed)
            return;
        m_disposed = true;
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

    private static EditorHistoryResult MoveAsset(
        AssetEditor editor,
        AssetEditorContext context,
        string sourcePath,
        string targetPath)
    {
        AssetManager.Move(sourcePath, targetPath);
        _ = context.interactions
            .For(
                FileBrowserAreas.Browser,
                AssetManager.TryGetFileSystemEntry(targetPath, out AssetFileEntry entry) ? entry : null)
            .Select();
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

    private static EditorValidationResult ToEditorValidation(AssetOperationValidation validation)
        => new(validation.isValid, validation.message);

    private static string Normalize(string? path)
        => string.IsNullOrWhiteSpace(path)
            ? string.Empty
            : path.Replace('\\', '/').Trim('/');
}
