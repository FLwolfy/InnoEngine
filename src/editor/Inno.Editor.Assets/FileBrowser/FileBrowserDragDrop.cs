using Inno.Editor.Assets;

using Inno.Editor.Assets.DragDrop;

using System;
using Inno.Assets.File;
using Inno.Editor.Core;
using Inno.Editor.Assets.AssetEditors;
using Inno.Editor.Core.DragDrop;
using Inno.Editor.ImGui;
using NativeImGui = Inno.Native.ImGui.ImGui;

namespace Inno.Editor.Assets.FileBrowser;

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
            new EditorDragContext(context, typeof(AssetSurface.Browser), data),
            () => NativeImGui.TextUnformatted(data.label));
    }

    internal void DrawDirectoryTarget(EditorContext context)
    {
        _ = EditorDragDropRenderer.Target(
            context,
            typeof(AssetSurface.Browser),
            new AssetDirectoryDropTarget(assets.browser.currentDirectory));
    }
}
