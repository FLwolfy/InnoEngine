using Inno.Editor.Assets.Selection;
using Inno.Editor.Core;
using Inno.Editor.Core.Menus;
using Inno.Editor.ImGui;
using Inno.Editor.ImGui.Renderers;
using Inno.Native.ImGui;
using static Inno.Editor.Assets.FileBrowser.FileBrowserUtility;
using NativeImGui = Inno.Native.ImGui.ImGui;

namespace Inno.Editor.Assets.FileBrowser;

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
            new EditorMenuContext(
                context,
                typeof(AssetSurface.ContextMenu),
                new AssetSelectionTarget(relativePath)));
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
            CreateDirectoryContext(context, relativePath));
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
            CreateDirectoryContext(context, assets.browser.currentDirectory));
        if (isOpen)
            rename.MarkInteraction(presentation);
    }

    private static EditorMenuContext CreateDirectoryContext(
        EditorContext context,
        string relativePath)
        => new(
            context,
            typeof(AssetSurface.ContextMenu),
            new AssetDirectoryTarget(NormalizePath(relativePath)));
}
