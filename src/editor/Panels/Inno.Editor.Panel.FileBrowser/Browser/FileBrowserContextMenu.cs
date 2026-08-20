using Inno.Assets;
using Inno.Assets.File;
using Inno.Editor.Core;
using Inno.Editor.ImGui;
using Inno.Native.ImGui;
using static Inno.Editor.Panel.FileBrowser.FileBrowserUtility;
using NativeImGui = Inno.Native.ImGui.ImGui;

namespace Inno.Editor.Panel.FileBrowser;

internal sealed class FileBrowserContextMenu(
    AssetEditorModule assets,
    FileBrowserRename rename)
{
    internal void DrawEntry(
        EditorContext context,
        string id,
        string relativePath,
        FileBrowserPresentation presentation)
    {
        if (NativeImGui.IsItemClicked(ImGuiMouseButton.Right))
            assets.browser.Select(context, relativePath);
        bool isOpen = EditorMenuRenderer.ContextMenu(
            id,
            assets.interactions.For(
                FileBrowserAreas.Browser,
                AssetManager.TryGetFileSystemEntry(relativePath, out AssetFileEntry entry) ? entry : null));
        if (isOpen)
            rename.MarkInteraction(presentation);
    }

    internal void DrawDirectory(
        EditorContext context,
        string id,
        string relativePath,
        FileBrowserPresentation presentation)
    {
        bool isOpen = EditorMenuRenderer.ContextMenu(
            id,
            CreateDirectoryInteraction(relativePath));
        if (isOpen)
            rename.MarkInteraction(presentation);
    }

    internal void DrawBackground(
        EditorContext context,
        string id,
        FileBrowserPresentation presentation)
    {
        bool isOpen = EditorMenuRenderer.WindowContextMenu(
            id,
            CreateDirectoryInteraction(assets.browser.currentDirectory));
        if (isOpen)
            rename.MarkInteraction(presentation);
    }

    private Inno.Editor.Interactions.EditorInteraction CreateDirectoryInteraction(string relativePath)
        => assets.interactions.For(
            FileBrowserAreas.Browser,
            NormalizePath(relativePath));
}
