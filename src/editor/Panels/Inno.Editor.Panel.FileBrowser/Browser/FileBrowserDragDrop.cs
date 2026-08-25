
using System;
using Inno.Assets.File;
using Inno.Editor.Core;
using Inno.Editor.Interactions;
using Inno.Editor.ImGui;
using EditorWidget = Inno.Editor.ImGui.ImGuiWidget.ImGuiWidget;
using NativeImGui = Inno.Native.ImGui.ImGui;

namespace Inno.Editor.Panel.FileBrowser;

/// <summary>
/// Binds managed File Browser drag sessions to native entry and directory items.
/// </summary>
internal sealed class FileBrowserDragDrop(AssetEditorModule assets)
{
    /// <summary>
    /// Publishes the supplied source entry as a managed File Browser drag payload.
    /// </summary>
    /// <param name="context">The current Editor context.</param>
    /// <param name="entry">The source entry bound to the last native item.</param>
    internal void DrawAssetSource(EditorContext context, AssetFileEntry entry)
    {
        if (!assets.TryCreateContext(
                context,
                entry.relativePath,
                out AssetEditorContext? assetContext) ||
            assetContext is null ||
            !assets.TryCreateDragData(assetContext, out EditorDragData? data) ||
            data is null)
            return;
        _ = EditorDragDropRenderer.Source(
            assets.interactions.For(FileBrowserInteractionIds.area, data.source),
            data,
            () => NativeImGui.TextUnformatted(data.label));
    }

    /// <summary>
    /// Accepts compatible File Browser payloads into a directory bound to the last native item.
    /// </summary>
    /// <param name="context">The current Editor context.</param>
    /// <param name="relativePath">The target directory, or <see langword="null"/> for the current directory.</param>
    internal void DrawDirectoryTarget(
        EditorContext context,
        string? relativePath = null)
    {
        System.Numerics.Vector2 minimum = NativeImGui.GetItemRectMin();
        System.Numerics.Vector2 maximum = NativeImGui.GetItemRectMax();
        EditorDropWidgetResult result = EditorDragDropRenderer.Target(
            assets.interactions.For(
                FileBrowserInteractionIds.area,
                relativePath ?? assets.browser.currentDirectory));
        if (!result.isPreviewing || !result.status.canDrop)
            return;

        EditorWidget.DropTargetHighlight(minimum, maximum);
    }
}
