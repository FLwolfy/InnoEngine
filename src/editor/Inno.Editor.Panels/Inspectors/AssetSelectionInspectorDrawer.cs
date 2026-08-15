using System.Numerics;

using Inno.Assets;
using Inno.Assets.File;
using Inno.Editor.Core;
using Inno.Editor.Inspection;
using NativeImGui = Inno.Native.ImGui.ImGui;

namespace Inno.Editor.Panels.Inspectors;

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
                new Vector4(1f, 0.35f, 0.35f, 1f),
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
