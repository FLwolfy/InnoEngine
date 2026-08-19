using Inno.Editor.Assets;
using Inno.Editor.Assets.DragDrop;
using Inno.Editor.Assets.Selection;

using Inno.Assets;
using Inno.Assets.File;
using Inno.Editor.Core;
using Inno.Editor.Core.Commands;
using Inno.Editor.ImGui;
using Inno.Editor.Scene.Inspection;
using NativeImGui = Inno.Native.ImGui.ImGui;

namespace Inno.Editor.Scene.Inspection;

[InspectorDrawer(typeof(AssetSelectionTarget))]
internal sealed class AssetSelectionInspectorDrawer : IInspectorDrawer
{
    /// <inheritdoc />
    public void Draw(InspectorDrawContext context)
    {
        var selection = (AssetSelectionTarget)context.target;
        if (!AssetManager.TryGetFileSystemEntry(selection.relativePath, out AssetFileEntry entry))
        {
            NativeImGui.TextColored(
                EditorPalette.error,
                "Selected entry no longer exists.");
            return;
        }

        DrawMetadata("Path", entry.relativePath);
        DrawMetadata("Type", entry.isDirectory ? "Directory" : "File");
        if (!entry.isDirectory)
        {
            DrawMetadata("Extension", string.IsNullOrEmpty(entry.extension) ? "<none>" : entry.extension);
        }
    }

    private static void DrawMetadata(string label, string value)
    {
        NativeImGui.TextUnformatted(label);
        NativeImGui.Separator();
        NativeImGui.TextUnformatted(value);
        NativeImGui.Spacing();
    }
}
