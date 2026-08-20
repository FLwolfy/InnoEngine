using Inno.Assets.File;
using Inno.Editor.Core;
using Inno.Editor.Interactions;
using Inno.Editor.ImGui;
using NativeImGui = Inno.Native.ImGui.ImGui;

namespace Inno.Editor.Panel.Inspector;

[InspectorDrawer(typeof(AssetFileEntry))]
internal sealed class AssetSelectionInspectorDrawer : IInspectorDrawer
{
    /// <inheritdoc />
    public void Draw(InspectorDrawContext context)
    {
        var entry = (AssetFileEntry)context.target;

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
