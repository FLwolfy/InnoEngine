using System;
using System.IO;

using Inno.Assets;
using Inno.Assets.Core;
using Inno.Assets.File;
using Inno.Core.Logging;
using Inno.Editor.Assets.AssetEditors;
using Inno.Editor.Assets.DragDrop;
using Inno.Editor.Assets.Selection;
using Inno.Editor.Core;
using Inno.Editor.Core.Commands;
using Inno.Editor.Core.DragDrop;

namespace Inno.Editor.Assets;

/// <summary>Owns shared Asset Browser state and asset-type extension dispatch.</summary>
[EditorModule(order: 100)]
public sealed class AssetEditorModule : EditorModule, IDisposable
{
    private readonly AssetEditorRegistry m_editors = new();
    private bool m_disposed;

    /// <summary>Gets shared Asset Browser navigation and selection state.</summary>
    public AssetBrowserState browser { get; } = new();

    internal EditorRenameSession? rename { get; private set; }

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

    internal void BeginRename(AssetEditorContext context)
    {
        CancelRename();
        string sourcePath = Normalize(context.relativePath);
        AssetEditor editor = m_editors.Resolve(context.assetType);
        rename = new EditorRenameSession(
            new AssetSelectionTarget(sourcePath),
            context.name,
            value => ToEditorValidation(ValidateRename(
                editor,
                context,
                sourcePath,
                value,
                validateEditor: true,
                out _)),
            value => CommitRename(editor, context, sourcePath, value));
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
        browser.Select(context.editorContext, null);
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

    internal void CancelRename()
    {
        rename?.Cancel();
        rename = null;
    }

    /// <inheritdoc />
    protected override void OnUpdate(EditorContext context)
    {
        if (rename?.isCompleted == true)
            rename = null;
    }

    /// <inheritdoc />
    protected override void OnStop(EditorContext context)
    {
        CancelRename();
    }

    /// <summary>
    /// Cancels active rename state and releases the current asset-editor registry snapshot.
    /// </summary>
    public void Dispose()
    {
        if (m_disposed)
            return;
        m_disposed = true;
        CancelRename();
        m_editors.Dispose();
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

    private void CommitRename(
        AssetEditor editor,
        AssetEditorContext context,
        string sourcePath,
        string newName)
    {
        AssetOperationValidation validation = ValidateRename(
            editor,
            context,
            sourcePath,
            newName,
            validateEditor: false,
            out string targetPath);
        if (!validation.isValid)
            throw new InvalidOperationException(validation.message);
        AssetManager.Move(sourcePath, targetPath);
        browser.Select(context.editorContext, targetPath);
        try
        {
            editor.OnRenamed(context, sourcePath, targetPath);
        }
        catch (Exception exception)
        {
            Log.Error("Asset editor rename hook failed for '{0}': {1}", targetPath, exception);
        }
    }

    private static EditorValidationResult ToEditorValidation(AssetOperationValidation validation)
        => new(validation.isValid, validation.message);

    private static string Normalize(string? path)
        => string.IsNullOrWhiteSpace(path)
            ? string.Empty
            : path.Replace('\\', '/').Trim('/');
}
