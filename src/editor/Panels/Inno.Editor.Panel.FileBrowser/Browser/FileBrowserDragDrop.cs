
using System;
using Inno.Assets.File;
using Inno.Editor.Core;
using Inno.Editor.Interactions;
using Inno.Editor.ImGui;
using NativeImGui = Inno.Native.ImGui.ImGui;

namespace Inno.Editor.Panel.FileBrowser;

internal sealed class FileBrowserDragDrop(AssetEditorModule assets)
{
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
            assets.interactions.For(FileBrowserAreas.Browser, data.source),
            data,
            () => NativeImGui.TextUnformatted(data.label));
    }

    internal void DrawDirectoryTarget(EditorContext context)
    {
        _ = EditorDragDropRenderer.Target(
            assets.interactions.For(
                FileBrowserAreas.Browser,
                assets.browser.currentDirectory));
    }
}
